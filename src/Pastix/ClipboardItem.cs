using System;

namespace Pastix
{
    /// <summary>
    /// 剪贴板历史中的一条记录。v0.1 仅文本 + 捕获时间。
    /// </summary>
    internal sealed class ClipboardItem
    {
        public string Text { get; }
        public DateTime CapturedAt { get; }

        public ClipboardItem(string text, DateTime capturedAt)
        {
            Text = text;
            CapturedAt = capturedAt;
        }
    }
}
