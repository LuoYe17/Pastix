using System;

namespace Pastix
{
    /// <summary>
    /// 剪贴板条目类型：纯文本或位图。
    /// </summary>
    internal enum ClipboardItemType
    {
        Text,
        Image,
    }

    /// <summary>
    /// 剪贴板历史中的一条记录。判别联合：根据 Type 决定 Text 或 Image* 字段有效。
    /// </summary>
    internal sealed class ClipboardItem
    {
        public ClipboardItemType Type { get; }
        public DateTime CapturedAt { get; }

        /// <summary>
        /// 是否被用户置顶。Pinned 项不计入 Max*Items 上限，并在列表中始终展示在顶部分组。
        /// </summary>
        public bool Pinned { get; set; }

        // ---- Text 类型独有 ----
        public string Text { get; }

        // ---- Image 类型独有 ----
        /// <summary>原图 PNG 字节流。</summary>
        public byte[] ImageBytes { get; }
        /// <summary>预生成的缩略图 PNG 字节流（不超过 180×120）。</summary>
        public byte[] ThumbnailBytes { get; }
        /// <summary>ImageBytes 的 SHA-256 十六进制哈希（64 字符），用于查重与粘贴回环抑制。</summary>
        public string ImageHash { get; }
        /// <summary>原图像素宽度（仅 Image 类型有效）。</summary>
        public int ImageWidth { get; }
        /// <summary>原图像素高度（仅 Image 类型有效）。</summary>
        public int ImageHeight { get; }

        private ClipboardItem(
            ClipboardItemType type,
            DateTime capturedAt,
            bool pinned,
            string text,
            byte[] imageBytes,
            byte[] thumbnailBytes,
            string imageHash,
            int width,
            int height)
        {
            Type = type;
            CapturedAt = capturedAt;
            Pinned = pinned;
            Text = text;
            ImageBytes = imageBytes;
            ThumbnailBytes = thumbnailBytes;
            ImageHash = imageHash;
            ImageWidth = width;
            ImageHeight = height;
        }

        public static ClipboardItem CreateText(string text, DateTime capturedAt, bool pinned = false)
        {
            return new ClipboardItem(
                ClipboardItemType.Text,
                capturedAt,
                pinned,
                text ?? string.Empty,
                null,
                null,
                null,
                0,
                0);
        }

        public static ClipboardItem CreateImage(
            byte[] png,
            byte[] thumbnail,
            string hash,
            int width,
            int height,
            DateTime capturedAt,
            bool pinned = false)
        {
            return new ClipboardItem(
                ClipboardItemType.Image,
                capturedAt,
                pinned,
                null,
                png,
                thumbnail,
                hash,
                width,
                height);
        }
    }
}
