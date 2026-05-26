using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Pastix.Native;

namespace Pastix
{
    /// <summary>
    /// 监听系统剪贴板变化，维护一个内存里的最近文本历史（最新在前）。
    /// v0.1：仅文本，最多 100 条，重复条目上移并刷新捕获时间。
    /// </summary>
    internal sealed class ClipboardWatcher : IDisposable
    {
        private const int MaxItems = 100;

        private readonly IntPtr _hwnd;
        private readonly LinkedList<ClipboardItem> _items = new LinkedList<ClipboardItem>();
        private bool _registered;
        private string _lastSeen; // 抑制 SetText 自身触发回环

        public event Action Changed;

        public ClipboardWatcher(IntPtr hwnd)
        {
            _hwnd = hwnd;
        }

        public void Start()
        {
            if (_registered) return;
            _registered = NativeMethods.AddClipboardFormatListener(_hwnd);

            // 启动时抓一次当前剪贴板内容
            TryCaptureCurrent();
        }

        public void OnClipboardUpdate()
        {
            TryCaptureCurrent();
        }

        /// <summary>调用方在写回剪贴板前后用此屏蔽回环：内容相同则直接忽略下次 update。</summary>
        public void SuppressNext(string text)
        {
            _lastSeen = text;
        }

        public IReadOnlyList<ClipboardItem> Snapshot()
        {
            var arr = new ClipboardItem[_items.Count];
            int i = 0;
            foreach (var item in _items) arr[i++] = item;
            return arr;
        }

        private void TryCaptureCurrent()
        {
            string text = null;
            try
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
            }
            catch
            {
                // 剪贴板偶发被其它进程独占，跳过本次
                return;
            }

            if (string.IsNullOrEmpty(text)) return;
            if (text == _lastSeen) return;
            _lastSeen = text;

            // 已存在则上移到首位（同时刷新时间）
            for (var node = _items.First; node != null; node = node.Next)
            {
                if (node.Value.Text == text)
                {
                    _items.Remove(node);
                    break;
                }
            }
            _items.AddFirst(new ClipboardItem(text, DateTime.Now));
            while (_items.Count > MaxItems) _items.RemoveLast();

            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (_registered)
            {
                NativeMethods.RemoveClipboardFormatListener(_hwnd);
                _registered = false;
            }
        }
    }
}
