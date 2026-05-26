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
        private const int SaveDebounceMs = 200;

        /// <summary>历史保留的最大条数。外部修改后可调用 <see cref="Trim"/> 立即裁剪。</summary>
        public int MaxItems { get; set; } = 100;

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

        /// <summary>
        /// 裁剪到当前 <see cref="MaxItems"/>。多余条目从尾部丢弃，
        /// 仅在确实丢弃时调度一次落盘（沿用现有防抖语义，不另开通道）。
        /// </summary>
        public void Trim()
        {
            bool changed = false;
            while (_items.Count > MaxItems)
            {
                _items.RemoveLast();
                changed = true;
            }
            if (changed)
            {
                ScheduleSave();
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// 清空全部历史并立刻删除磁盘文件，用于设置面板的"清空所有历史"按钮。
        /// </summary>
        public void ClearAll()
        {
            _items.Clear();
            _lastSeen = null;
            // 取消任何挂起的保存，避免 Trim 后又把空集合写回
            _saveTimer?.Stop();
            HistoryStore.Delete();
            Changed?.Invoke();
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
