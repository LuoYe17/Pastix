using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Pastix.Native;

namespace Pastix
{
    /// <summary>
    /// 监听系统剪贴板变化，维护一个内存里的最近文本历史（最新在前）。
    /// v0.2：启动时从 history.dat 恢复，每次变化后 200ms 防抖落盘（DPAPI 加密）。
    /// </summary>
    internal sealed class ClipboardWatcher : IDisposable
    {
        private const int MaxItems = 100;
        private const int SaveDebounceMs = 200;

        private readonly IntPtr _hwnd;
        private readonly LinkedList<ClipboardItem> _items = new LinkedList<ClipboardItem>();
        private System.Windows.Forms.Timer _saveTimer;
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

            // 先恢复历史，再注册监听并捕获当前剪贴板
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

            ScheduleSave();
            Changed?.Invoke();
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
                // 退出前若有未落盘的变更，立即同步一次，避免丢失最新条目
                if (pending) HistoryStore.Save(Snapshot());
            }
        }
    }
}
