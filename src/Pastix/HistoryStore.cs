using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Pastix
{
    /// <summary>
    /// 把剪贴板历史以 DPAPI 加密形式持久化到 exe 同目录的 history.dat。
    /// 文件头：'P''S''T''X' + 版本(BE u16) + 条数(LE i32) + 条目*
    ///   v1 (0x0001) 每条：CapturedAt.Ticks(LE i64) + payloadLen(LE i32) + DPAPI(UTF8)
    ///   v2 (0x0002) 每条：CapturedAt.Ticks(LE i64) + Pinned(u8 0/1) + payloadLen(LE i32) + DPAPI(UTF8)
    /// Load 兼容 v1 / v2，Save 永远写 v2。
    /// 所有失败均静默：load 失败返回空列表且不删源文件，save 失败下次再写。
    /// </summary>
    internal static class HistoryStore
    {
        private const ushort VersionV1 = 0x0001;
        private const ushort VersionV2 = 0x0002;
        private const int MaxItems = 10_000;             // 防御性上限，避免坏文件触发巨量分配
        private const int MaxPayloadBytes = 16 * 1024 * 1024;

        private static string FilePath
        {
            get
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                return Path.Combine(dir, "history.dat");
            }
        }

        public static List<ClipboardItem> Load()
        {
            var result = new List<ClipboardItem>();
            string path = FilePath;
            if (!File.Exists(path)) return result;

            try
            {
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    byte[] magic = br.ReadBytes(4);
                    if (magic.Length != 4 || magic[0] != (byte)'P' || magic[1] != (byte)'S'
                        || magic[2] != (byte)'T' || magic[3] != (byte)'X')
                        return Empty(result);

                    int hi = br.ReadByte();
                    int lo = br.ReadByte();
                    int ver = (hi << 8) | lo;
                    bool v2 = ver == VersionV2;
                    if (ver != VersionV1 && !v2) return Empty(result);

                    int count = br.ReadInt32();
                    if (count < 0 || count > MaxItems) return Empty(result);

                    for (int i = 0; i < count; i++)
                    {
                        long ticks = br.ReadInt64();
                        bool pinned = false;
                        if (v2)
                        {
                            byte pin = br.ReadByte();
                            pinned = pin != 0;
                        }
                        int payloadLen = br.ReadInt32();
                        if (payloadLen < 0 || payloadLen > MaxPayloadBytes) return Empty(result);

                        byte[] payload = br.ReadBytes(payloadLen);
                        if (payload.Length != payloadLen) return Empty(result);

                        byte[] plain = ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
                        string text = Encoding.UTF8.GetString(plain);
                        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                            return Empty(result);
                        result.Add(new ClipboardItem(text, new DateTime(ticks), pinned));
                    }
                }
                return result;
            }
            catch
            {
                return Empty(result);
            }
        }

        public static void Save(IReadOnlyList<ClipboardItem> items)
        {
            string path = FilePath;
            string tmp = path + ".tmp";
            try
            {
                using (var fs = File.Create(tmp))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write((byte)'P');
                    bw.Write((byte)'S');
                    bw.Write((byte)'T');
                    bw.Write((byte)'X');
                    bw.Write((byte)((VersionV2 >> 8) & 0xFF));
                    bw.Write((byte)(VersionV2 & 0xFF));
                    bw.Write(items.Count);

                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        bw.Write(it.CapturedAt.Ticks);
                        bw.Write(it.Pinned ? (byte)1 : (byte)0);
                        byte[] plain = Encoding.UTF8.GetBytes(it.Text ?? string.Empty);
                        byte[] payload = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                        bw.Write(payload.Length);
                        bw.Write(payload);
                    }
                    bw.Flush();
                }

                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 静默 */ }
            }
        }

        private static List<ClipboardItem> Empty(List<ClipboardItem> r)
        {
            r.Clear();
            return r;
        }

        /// <summary>
        /// 删除磁盘上的历史文件。失败静默：用户即使关掉 Pastix 也能手动删 history.dat。
        /// </summary>
        public static void Delete()
        {
            try
            {
                string path = FilePath;
                if (File.Exists(path)) File.Delete(path);
                string tmp = path + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
                // 文件被占用或权限不足时安静放弃
            }
        }
    }
}
