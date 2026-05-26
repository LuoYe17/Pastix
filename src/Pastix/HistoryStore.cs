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
    ///   v2 (0x0002) 每条：CapturedAt.Ticks(LE i64) + Pinned(u8) + payloadLen(LE i32) + DPAPI(UTF8)
    ///   v3 (0x0003) 每条：Type(u8 0=text 1=image) + CapturedAt.Ticks(LE i64) + Pinned(u8)
    ///                    + payloadLen(LE i32) + DPAPI(payload)
    ///                    + thumbLen(LE i32) + DPAPI(thumb)        // image 时 thumbLen>0
    /// Load 兼容 v1/v2/v3，Save 永远写 v3。
    /// 文本损坏 → abort 整文件返回空（防止错位读乱码）；
    /// v3 中图片单条解码失败仅跳过该条目（不影响其它条目）。
    /// </summary>
    internal static class HistoryStore
    {
        private const ushort VersionV1 = 0x0001;
        private const ushort VersionV2 = 0x0002;
        private const ushort VersionV3 = 0x0003;
        private const int MaxItems = 10_000;
        private const int MaxTextPayloadBytes = 16 * 1024 * 1024;
        private const int MaxImageBytes = 20 * 1024 * 1024;
        private const int MaxThumbBytes = 200 * 1024;

        private const byte TypeText = 0;
        private const byte TypeImage = 1;

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
                    if (ver != VersionV1 && ver != VersionV2 && ver != VersionV3) return Empty(result);

                    int count = br.ReadInt32();
                    if (count < 0 || count > MaxItems) return Empty(result);

                    for (int i = 0; i < count; i++)
                    {
                        if (ver == VersionV3)
                        {
                            if (!TryReadV3Item(br, result)) return Empty(result);
                        }
                        else
                        {
                            // v1/v2：纯文本
                            long ticks = br.ReadInt64();
                            bool pinned = false;
                            if (ver == VersionV2)
                            {
                                byte pin = br.ReadByte();
                                pinned = pin != 0;
                            }
                            int payloadLen = br.ReadInt32();
                            if (payloadLen < 0 || payloadLen > MaxTextPayloadBytes) return Empty(result);
                            byte[] payload = br.ReadBytes(payloadLen);
                            if (payload.Length != payloadLen) return Empty(result);
                            byte[] plain = ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
                            string text = Encoding.UTF8.GetString(plain);
                            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                                return Empty(result);
                            result.Add(ClipboardItem.CreateText(text, new DateTime(ticks), pinned));
                        }
                    }
                }
                return result;
            }
            catch
            {
                return Empty(result);
            }
        }

        /// <summary>
        /// 读取单条 v3 条目：
        ///  - 文本损坏（DPAPI/UTF8）抛出异常 → 让外层 abort 整文件
        ///  - 图片解码失败时安静跳过该条（返回 true，不向 result 添加）
        /// </summary>
        private static bool TryReadV3Item(BinaryReader br, List<ClipboardItem> result)
        {
            byte type = br.ReadByte();
            long ticks = br.ReadInt64();
            byte pin = br.ReadByte();
            bool pinned = pin != 0;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
            var capturedAt = new DateTime(ticks);

            int payloadLen = br.ReadInt32();
            int upper = type == TypeImage ? MaxImageBytes : MaxTextPayloadBytes;
            if (payloadLen < 0 || payloadLen > upper) return false;
            byte[] payload = br.ReadBytes(payloadLen);
            if (payload.Length != payloadLen) return false;

            int thumbLen = br.ReadInt32();
            int thumbUpper = type == TypeImage ? MaxThumbBytes : 0;
            if (thumbLen < 0 || thumbLen > thumbUpper) return false;
            byte[] thumbEnc = br.ReadBytes(thumbLen);
            if (thumbEnc.Length != thumbLen) return false;

            if (type == TypeText)
            {
                // 文本失败 → 让异常抛出，外层 catch 返回空（不容忍错位）
                byte[] plain = ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
                string text = Encoding.UTF8.GetString(plain);
                result.Add(ClipboardItem.CreateText(text, capturedAt, pinned));
            }
            else if (type == TypeImage)
            {
                // 图片单条容错：解密或解析失败仅跳过
                try
                {
                    byte[] png = ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
                    byte[] thumb = thumbLen > 0
                        ? ProtectedData.Unprotect(thumbEnc, null, DataProtectionScope.CurrentUser)
                        : null;
                    if (thumb == null) return true; // 缩略图缺失视为坏条目，跳过
                    string hash = Sha256Hex(png);
                    PngHeader.TryReadSize(png, out int w, out int h);
                    result.Add(ClipboardItem.CreateImage(png, thumb, hash, w, h, capturedAt, pinned));
                }
                catch
                {
                    // 单条图片损坏 → 跳过
                }
            }
            else
            {
                // 未知类型，丢弃整文件以防错位
                return false;
            }
            return true;
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
                    bw.Write((byte)((VersionV3 >> 8) & 0xFF));
                    bw.Write((byte)(VersionV3 & 0xFF));
                    bw.Write(items.Count);

                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        if (it.Type == ClipboardItemType.Text)
                        {
                            bw.Write(TypeText);
                            bw.Write(it.CapturedAt.Ticks);
                            bw.Write(it.Pinned ? (byte)1 : (byte)0);
                            byte[] plain = Encoding.UTF8.GetBytes(it.Text ?? string.Empty);
                            byte[] payload = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                            bw.Write(payload.Length);
                            bw.Write(payload);
                            bw.Write(0); // thumbLen
                        }
                        else
                        {
                            bw.Write(TypeImage);
                            bw.Write(it.CapturedAt.Ticks);
                            bw.Write(it.Pinned ? (byte)1 : (byte)0);
                            byte[] payload = ProtectedData.Protect(
                                it.ImageBytes ?? Array.Empty<byte>(), null, DataProtectionScope.CurrentUser);
                            bw.Write(payload.Length);
                            bw.Write(payload);
                            byte[] thumbEnc = ProtectedData.Protect(
                                it.ThumbnailBytes ?? Array.Empty<byte>(), null, DataProtectionScope.CurrentUser);
                            bw.Write(thumbEnc.Length);
                            bw.Write(thumbEnc);
                        }
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

        private static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data);
                var sb = new StringBuilder(h.Length * 2);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>从 PNG 文件头读取像素尺寸。失败返回 (0,0)。</summary>
        private static class PngHeader
        {
            public static bool TryReadSize(byte[] png, out int width, out int height)
            {
                width = 0; height = 0;
                // 8 字节签名 + IHDR(长度4 + "IHDR"4 + 宽4 + 高4 ...)
                if (png == null || png.Length < 24) return false;
                if (png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47) return false;
                if (png[12] != (byte)'I' || png[13] != (byte)'H' || png[14] != (byte)'D' || png[15] != (byte)'R')
                    return false;
                width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
                height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
                if (width <= 0 || height <= 0) { width = height = 0; return false; }
                return true;
            }
        }
    }
}
