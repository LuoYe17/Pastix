using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Pastix.Native;

namespace Pastix
{
    /// <summary>
    /// 监听系统剪贴板变化，维护一个内存里的最近文本/图片历史（最新在前）。
    /// 启动时从 history.dat 恢复，每次变化后 200ms 防抖落盘（DPAPI 加密）。
    /// 上限策略：
    ///   - MaxItems：仅限制非 pin 文本条目数量
    ///   - MaxImageItems：仅限制非 pin 图片条目数量
    ///   - MaxTotalBytes：所有条目（含 pinned）总字节占用，超出则 evict 最老非 pin
    /// pinned 永远保留；当所有非 pin 条目都已驱逐仍超盘则停止驱逐。
    /// </summary>
    internal sealed class ClipboardWatcher : IDisposable
    {
        private const int SaveDebounceMs = 200;
        private const int ThumbMaxW = 180;
        private const int ThumbMaxH = 120;

        /// <summary>非 pin 文本条目上限。</summary>
        public int MaxItems { get; set; } = 100;

        /// <summary>非 pin 图片条目上限。</summary>
        public int MaxImageItems { get; set; } = Settings.DefaultMaxImageItems;

        /// <summary>总磁盘字节占用（文本 UTF-8 字节 + 图片原图字节 + 缩略图字节，含 pinned）。</summary>
        public long MaxTotalBytes { get; set; } = (long)Settings.DefaultMaxTotalMB * 1024 * 1024;

        private readonly IntPtr _hwnd;
        private readonly LinkedList<ClipboardItem> _items = new LinkedList<ClipboardItem>();
        private System.Windows.Forms.Timer _saveTimer;
        private bool _registered;
        private string _lastSeenText;
        private string _lastSeenImageHash;

        public event Action Changed;

        public ClipboardWatcher(IntPtr hwnd)
        {
            _hwnd = hwnd;
        }

        public void Start()
        {
            if (_registered) return;

            foreach (var item in HistoryStore.Load()) _items.AddLast(item);

            _saveTimer = new System.Windows.Forms.Timer { Interval = SaveDebounceMs };
            _saveTimer.Tick += OnSaveTick;

            _registered = NativeMethods.AddClipboardFormatListener(_hwnd);

            TryCaptureCurrent();
        }

        public void OnClipboardUpdate()
        {
            TryCaptureCurrent();
        }

        /// <summary>调用方在 SetText 前调用，避免 SetText 自身触发的 update 把同样内容再次推入。</summary>
        public void SuppressNext(string text)
        {
            _lastSeenText = text;
        }

        /// <summary>调用方在 SetImage 前调用，避免 SetImage 自身触发的 update 把同样图片再次推入。</summary>
        public void SuppressNextImage(string hash)
        {
            _lastSeenImageHash = hash;
        }

        public IReadOnlyList<ClipboardItem> Snapshot()
        {
            var arr = new ClipboardItem[_items.Count];
            int i = 0;
            foreach (var item in _items)
                if (item.Pinned) arr[i++] = item;
            foreach (var item in _items)
                if (!item.Pinned) arr[i++] = item;
            return arr;
        }

        /// <summary>外部修改限制后立即裁剪，行为等价于一次 EnforceLimits。</summary>
        public void Trim()
        {
            if (EnforceLimits())
            {
                ScheduleSave();
                Changed?.Invoke();
            }
        }

        public void TogglePin(ClipboardItem item)
        {
            if (item == null) return;
            var node = FindNode(item);
            if (node == null) return;
            node.Value.Pinned = !node.Value.Pinned;
            ScheduleSave();
            Changed?.Invoke();
        }

        public void RemoveItem(ClipboardItem item)
        {
            if (item == null) return;
            var node = FindNode(item);
            if (node == null) return;
            _items.Remove(node);
            ScheduleSave();
            Changed?.Invoke();
        }

        public void ClearAll()
        {
            _items.Clear();
            _lastSeenText = null;
            _lastSeenImageHash = null;
            _saveTimer?.Stop();
            HistoryStore.Delete();
            Changed?.Invoke();
        }

        private LinkedListNode<ClipboardItem> FindNode(ClipboardItem item)
        {
            for (var n = _items.First; n != null; n = n.Next)
                if (ReferenceEquals(n.Value, item)) return n;
            return null;
        }

        private void TryCaptureCurrent()
        {
            // 优先级：图片优先。某些应用（如 Office）会同时塞图片+文本元数据，按图片处理更符合用户意图。
            try
            {
                if (Clipboard.ContainsImage())
                {
                    CaptureImage();
                    return;
                }
                if (Clipboard.ContainsText())
                {
                    CaptureText();
                    return;
                }
            }
            catch
            {
                // 剪贴板被独占时跳过本次
            }
        }

        private void CaptureText()
        {
            string text;
            try { text = Clipboard.GetText(); }
            catch { return; }
            if (string.IsNullOrEmpty(text)) return;
            if (text == _lastSeenText) return;
            _lastSeenText = text;

            bool keepPinned = false;
            for (var node = _items.First; node != null; node = node.Next)
            {
                if (node.Value.Type == ClipboardItemType.Text && node.Value.Text == text)
                {
                    keepPinned = node.Value.Pinned;
                    _items.Remove(node);
                    break;
                }
            }
            _items.AddFirst(ClipboardItem.CreateText(text, DateTime.Now, keepPinned));

            EnforceLimits();
            ScheduleSave();
            Changed?.Invoke();
        }

        private void CaptureImage()
        {
            Image img = null;
            try { img = Clipboard.GetImage(); }
            catch { return; }
            if (img == null) return;

            try
            {
                byte[] pngBytes;
                int width, height;
                try
                {
                    width = img.Width;
                    height = img.Height;
                    pngBytes = EncodePng(img);
                }
                catch { return; }

                string hash = Sha256Hex(pngBytes);
                if (hash == _lastSeenImageHash) return;
                _lastSeenImageHash = hash;

                // 已存在则上移并刷新时间，保留 Pinned
                bool keepPinned = false;
                for (var node = _items.First; node != null; node = node.Next)
                {
                    if (node.Value.Type == ClipboardItemType.Image && node.Value.ImageHash == hash)
                    {
                        keepPinned = node.Value.Pinned;
                        _items.Remove(node);
                        break;
                    }
                }

                byte[] thumbBytes;
                try { thumbBytes = MakeThumbnail(img); }
                catch { return; }

                _items.AddFirst(ClipboardItem.CreateImage(
                    pngBytes, thumbBytes, hash, width, height, DateTime.Now, keepPinned));

                EnforceLimits();
                ScheduleSave();
                Changed?.Invoke();
            }
            finally
            {
                img.Dispose();
            }
        }

        /// <summary>
        /// 应用三类上限。返回是否实际驱逐过条目。仅 evict 非 pin。
        /// </summary>
        private bool EnforceLimits()
        {
            bool changed = false;
            // 安全上限：硬性循环 cap，避免坏数据导致死循环
            int safety = _items.Count + 1;
            while (safety-- > 0)
            {
                int textCount = 0, imageCount = 0;
                long totalBytes = 0;
                foreach (var it in _items)
                {
                    if (it.Type == ClipboardItemType.Text)
                    {
                        if (!it.Pinned) textCount++;
                        totalBytes += SizeOf(it);
                    }
                    else
                    {
                        if (!it.Pinned) imageCount++;
                        totalBytes += SizeOf(it);
                    }
                }

                ClipboardItemType? targetType = null;
                if (textCount > MaxItems) targetType = ClipboardItemType.Text;
                else if (imageCount > MaxImageItems) targetType = ClipboardItemType.Image;
                else if (totalBytes > MaxTotalBytes) targetType = null; // 任意类型都行

                if (targetType == null && totalBytes <= MaxTotalBytes) break;

                // 找最老的非 pin 条目（必要时按 type 过滤）
                var victim = _items.Last;
                while (victim != null)
                {
                    if (!victim.Value.Pinned &&
                        (targetType == null || victim.Value.Type == targetType.Value))
                        break;
                    victim = victim.Previous;
                }
                if (victim == null) break; // 全是 pinned，停止
                _items.Remove(victim);
                changed = true;
            }
            return changed;
        }

        private static long SizeOf(ClipboardItem it)
        {
            if (it.Type == ClipboardItemType.Text)
            {
                // UTF-8 字节估算（控制总盘，DPAPI 后会更大但作为估算够用）
                return it.Text == null ? 0 : Encoding.UTF8.GetByteCount(it.Text);
            }
            long n = 0;
            if (it.ImageBytes != null) n += it.ImageBytes.Length;
            if (it.ThumbnailBytes != null) n += it.ThumbnailBytes.Length;
            return n;
        }

        private void ScheduleSave()
        {
            if (_saveTimer == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void OnSaveTick(object sender, EventArgs e)
        {
            _saveTimer.Stop();
            HistoryStore.Save(Snapshot());
        }

        public void Dispose()
        {
            if (_registered)
            {
                NativeMethods.RemoveClipboardFormatListener(_hwnd);
                _registered = false;
            }

            if (_saveTimer != null)
            {
                bool pending = _saveTimer.Enabled;
                _saveTimer.Stop();
                _saveTimer.Dispose();
                _saveTimer = null;
                if (pending) HistoryStore.Save(Snapshot());
            }
        }

        // ---------------- 图片助手 ----------------

        private static byte[] EncodePng(Image src)
        {
            using (var ms = new MemoryStream())
            {
                src.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static byte[] MakeThumbnail(Image src)
        {
            int w = src.Width, h = src.Height;
            double scale = Math.Min((double)ThumbMaxW / w, (double)ThumbMaxH / h);
            if (scale > 1) scale = 1; // 不放大
            int tw = Math.Max(1, (int)(w * scale));
            int th = Math.Max(1, (int)(h * scale));
            using (var bmp = new Bitmap(tw, th, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, tw, th);
                }
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
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
    }
}
