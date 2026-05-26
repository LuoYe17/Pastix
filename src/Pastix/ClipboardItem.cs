using System;

namespace Pastix
{
    /// <summary>
    /// 剪贴板历史中的一条记录。文本 + 捕获时间 + 是否置顶。
    /// </summary>
    internal sealed class ClipboardItem
    {
        public string Text { get; }
        public DateTime CapturedAt { get; }

        /// <summary>
        /// 是否被用户置顶。Pinned 项不计入 MaxItems 上限，且在列表中始终展示在顶部分组。
        /// 运行时可变（用户操作切换）。
        /// </summary>
        public bool Pinned { get; set; }

        public ClipboardItem(string text, DateTime capturedAt) : this(text, capturedAt, false)
        {
        }

        public ClipboardItem(string text, DateTime capturedAt, bool pinned)
        {
            Text = text;
            CapturedAt = capturedAt;
            Pinned = pinned;
        }
    }
}
