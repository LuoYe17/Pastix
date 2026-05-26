using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Pastix.UI;

namespace Pastix
{
    /// <summary>
    /// 剪贴板历史浮窗：圆角磨砂深色卡片，顶部搜索框 + 列表 + 底部状态栏。
    /// 键盘上下导航、Enter 确认、Esc 关闭，单击列表项也可确认。
    /// </summary>
    internal sealed class HistoryWindow : Form
    {
        public event Action<string> ItemChosen;
        public event Action Cancelled;

        private const int CardWidth = 400;
        private const int CornerRadius = 12;
        private const int Pad = 12;
        private const int SearchHeight = 32;
        private const int RowHeight = 36;
        private const int StatusHeight = 28;
        private const int ListMaxHeight = 400;
        private const int ListEmptyHeight = 80;
        private const int PreviewMaxChars = 80;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly SearchBox _search;
        private readonly ListPanel _list;
        private readonly HintBar _status;
        private IReadOnlyList<ClipboardItem> _all = Array.Empty<ClipboardItem>();
        private readonly List<ClipboardItem> _filtered = new List<ClipboardItem>();
        private bool _suppressDeactivate;

        public HistoryWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(40, 40, 42); // 实色，圆角靠 Region 裁剪
            KeyPreview = true;

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _search = new SearchBox
            {
                Location = new Point(Pad, Pad),
                Size = new Size(CardWidth - Pad * 2, SearchHeight),
                PlaceholderText = "搜索剪贴板…",
            };
            _search.Inner.TextChanged += (s, e) => ApplyFilter();
            _search.Inner.KeyDown += OnSearchKeyDown;
            Controls.Add(_search);

            _list = new ListPanel
            {
                Location = new Point(Pad, Pad + SearchHeight + Pad),
                Size = new Size(CardWidth - Pad * 2, RowHeight),
                BackColor = BackColor,
            };
            _list.ItemActivated += idx => Choose(idx);
            Controls.Add(_list);

            _status = new HintBar
            {
                Dock = DockStyle.None,
                Height = StatusHeight,
            };
            Controls.Add(_status);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= WS_EX_TOOLWINDOW; return cp; }
        }

        public void ShowWith(IReadOnlyList<ClipboardItem> items, Point cursor)
        {
            _all = items ?? Array.Empty<ClipboardItem>();
            _suppressDeactivate = false;
            _search.Inner.Text = string.Empty;
            ApplyFilter(); // 内部会 RelayoutAndPosition

            var screen = Screen.FromPoint(cursor).WorkingArea;
            Location = new Point(
                screen.Left + (screen.Width - Width) / 2,
                screen.Top + (screen.Height - Height) / 2);

            Show();
            Activate();
            _search.Inner.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.ToolbarBorder, 1f))
            using (var path = GraphicsHelpers.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), CornerRadius))
                e.Graphics.DrawPath(pen, path);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_suppressDeactivate) return;
            _suppressDeactivate = true;
            Cancelled?.Invoke();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: _suppressDeactivate = true; Cancelled?.Invoke(); return true;
                case Keys.Down: _list.MoveSelection(1); return true;
                case Keys.Up: _list.MoveSelection(-1); return true;
                case Keys.Enter: Choose(_list.SelectedIndex); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Choose(int idx)
        {
            if (idx < 0 || idx >= _filtered.Count) return;
            _suppressDeactivate = true;
            ItemChosen?.Invoke(_filtered[idx].Text);
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            // 上下键已经在 ProcessCmdKey 处理；这里仅吃掉默认 ding 声
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void ApplyFilter()
        {
            string q = _search.Inner.Text;
            _filtered.Clear();
            foreach (var it in _all)
            {
                if (string.IsNullOrEmpty(q) ||
                    it.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    _filtered.Add(it);
            }
            _list.SetItems(_filtered, hasNoSource: _all.Count == 0);
            _status.SetCounts(_filtered.Count, _all.Count, isFiltering: !string.IsNullOrEmpty(q));
            RelayoutAndResize();
        }

        private void RelayoutAndResize()
        {
            int listH = _filtered.Count > 0
                ? Math.Min(_filtered.Count * RowHeight, ListMaxHeight)
                : ListEmptyHeight;

            int totalH = Pad + SearchHeight + Pad + listH + StatusHeight;

            int oldH = Height;
            Size = new Size(CardWidth, totalH);

            // 仅在窗口已可见时根据原中心微调位置；首次显示前由 ShowWith 重新居中
            if (Visible && oldH != totalH)
            {
                Top -= (totalH - oldH) / 2;
            }

            _list.Size = new Size(CardWidth - Pad * 2, listH);
            _status.SetBounds(0, totalH - StatusHeight, CardWidth, StatusHeight);

            using (var path = GraphicsHelpers.RoundRect(new RectangleF(0, 0, Width, Height), CornerRadius))
                Region = new Region(path);
            Invalidate();
        }

        // ---------------- 底部状态栏 ----------------

        private sealed class HintBar : Panel
        {
            private static readonly Color BarBg = Color.FromArgb(255, 30, 30, 32);
            private static readonly Color TextColor = Color.FromArgb(140, 255, 255, 255);
            private static readonly Color TopLine = Color.FromArgb(30, 255, 255, 255);
            private string _text = string.Empty;

            public HintBar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = BarBg;
            }

            public void SetCounts(int shown, int total, bool isFiltering)
            {
                _text = isFiltering
                    ? string.Format("{0}/{1} 条 · ↑↓ 选择 · Enter 粘贴 · Esc 关闭", shown, total)
                    : string.Format("{0} 条 · ↑↓ 选择 · Enter 粘贴 · Esc 关闭", total);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                using (var pen = new Pen(TopLine, 1f))
                    g.DrawLine(pen, 0, 0, Width, 0);

                using (var font = Theme.UiFont(8.5f))
                {
                    TextRenderer.DrawText(g, _text, font,
                        new Rectangle(Pad, 0, Width - Pad * 2, Height), TextColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }
            }
        }

        // ---------------- 自绘列表面板 ----------------

        private sealed class ListPanel : Panel
        {
            public event Action<int> ItemActivated;

            private static readonly Color HoverOverlay = Color.FromArgb(20, 255, 255, 255);
            private static readonly Color SelectedOverlay = Color.FromArgb(45, 10, 132, 255);
            private static readonly Color RowAccentBar = Theme.Accent;
            private static readonly Color SubTextColor = Color.FromArgb(160, 255, 255, 255);
            private static readonly Color EmptyTextColor = Color.FromArgb(140, 255, 255, 255);
            private const int LeftTextPad = 12;
            private const int RightTextPad = 12;
            private const int AccentBarWidth = 3;
            private const int TimeMaxWidth = 80;

            private readonly VScrollBar _scroll;
            private List<ClipboardItem> _items = new List<ClipboardItem>();
            private int _selected = -1;
            private int _hover = -1;
            private bool _hasNoSource;

            public ListPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Color.FromArgb(40, 40, 42);

                _scroll = new VScrollBar { Dock = DockStyle.Right, Width = 12, Visible = false };
                _scroll.Scroll += (s, e) => Invalidate();
                Controls.Add(_scroll);
            }

            public int SelectedIndex => _selected;

            public void SetItems(List<ClipboardItem> items, bool hasNoSource)
            {
                _items = items ?? new List<ClipboardItem>();
                _hasNoSource = hasNoSource;
                _selected = _items.Count > 0 ? 0 : -1;
                _hover = -1;
                UpdateScroll();
                EnsureVisible(_selected);
                Invalidate();
            }

            public void MoveSelection(int delta)
            {
                if (_items.Count == 0) return;
                int next = Math.Max(0, Math.Min(_items.Count - 1, _selected + delta));
                if (next == _selected) return;
                _selected = next;
                EnsureVisible(_selected);
                Invalidate();
            }

            private void UpdateScroll()
            {
                int contentH = _items.Count * RowHeight;
                if (contentH > Height)
                {
                    _scroll.Visible = true;
                    _scroll.Minimum = 0;
                    _scroll.Maximum = contentH;
                    _scroll.LargeChange = Math.Max(1, Height);
                    _scroll.SmallChange = RowHeight;
                    if (_scroll.Value > contentH - Height) _scroll.Value = Math.Max(0, contentH - Height);
                }
                else
                {
                    _scroll.Visible = false;
                    _scroll.Value = 0;
                }
            }

            private void EnsureVisible(int idx)
            {
                if (idx < 0 || !_scroll.Visible) return;
                int top = idx * RowHeight;
                int bottom = top + RowHeight;
                if (top < _scroll.Value)
                    _scroll.Value = Math.Max(0, top);
                else if (bottom > _scroll.Value + Height)
                    _scroll.Value = Math.Min(_scroll.Maximum - _scroll.LargeChange + 1, bottom - Height);
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                UpdateScroll();
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (!_scroll.Visible) return;
                int max = Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1);
                int v = _scroll.Value - (e.Delta / 120) * RowHeight * 2;
                _scroll.Value = Math.Max(0, Math.Min(max, v));
                Invalidate();
            }

            private int IndexAt(Point p)
            {
                if (_scroll.Visible && p.X >= Width - _scroll.Width) return -1;
                int idx = (p.Y + _scroll.Value) / RowHeight;
                return (idx < 0 || idx >= _items.Count) ? -1 : idx;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = IndexAt(e.Location);
                if (h != _hover) { _hover = h; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_hover != -1) { _hover = -1; Invalidate(); }
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                int idx = IndexAt(e.Location);
                if (idx >= 0) { _selected = idx; Invalidate(); ItemActivated?.Invoke(idx); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                int rightPad = _scroll.Visible ? _scroll.Width : 0;

                if (_items.Count == 0)
                {
                    string emptyMsg = _hasNoSource ? "还没有复制过任何内容" : "找不到匹配项";
                    using (var font = Theme.UiFont(9f))
                    {
                        TextRenderer.DrawText(g, emptyMsg, font,
                            new Rectangle(0, 0, Width - rightPad, Height), EmptyTextColor,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.NoPrefix);
                    }
                    return;
                }

                int viewTop = _scroll.Value;
                int firstIdx = Math.Max(0, viewTop / RowHeight);
                int lastIdx = Math.Min(_items.Count - 1, (viewTop + Height) / RowHeight);

                using (var font = Theme.UiFont(9.5f))
                using (var timeFont = Theme.UiFont(8.5f))
                {
                    for (int i = firstIdx; i <= lastIdx; i++)
                    {
                        int y = i * RowHeight - viewTop;
                        var rowRect = new Rectangle(0, y, Width - rightPad, RowHeight);

                        bool isSelected = (i == _selected);
                        bool isHover = (i == _hover);

                        // 选中底色（18% accent）
                        if (isSelected)
                        {
                            using (var brush = new SolidBrush(SelectedOverlay))
                                g.FillRectangle(brush, rowRect);
                            // 左侧 3px 蓝色竖条
                            using (var bar = new SolidBrush(RowAccentBar))
                                g.FillRectangle(bar, rowRect.X, rowRect.Y, AccentBarWidth, rowRect.Height);
                        }
                        // 悬停叠加（8% 白）
                        if (isHover)
                        {
                            using (var brush = new SolidBrush(HoverOverlay))
                                g.FillRectangle(brush, rowRect);
                        }

                        // 主文本
                        var item = _items[i];
                        string preview = TruncateForRow(item.Text);
                        string timeText = RelativeTime(item.CapturedAt);
                        int timeW = TextRenderer.MeasureText(timeText, timeFont).Width;
                        if (timeW > TimeMaxWidth) timeW = TimeMaxWidth;

                        var mainRect = new Rectangle(
                            rowRect.X + LeftTextPad,
                            rowRect.Y,
                            rowRect.Width - LeftTextPad - RightTextPad - timeW - 8,
                            rowRect.Height);
                        TextRenderer.DrawText(g, preview, font, mainRect, Theme.IconColorActive,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                        // 右侧相对时间
                        var timeRect = new Rectangle(
                            rowRect.Right - RightTextPad - timeW,
                            rowRect.Y,
                            timeW,
                            rowRect.Height);
                        TextRenderer.DrawText(g, timeText, timeFont, timeRect, SubTextColor,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.NoPrefix);
                    }
                }
            }

            private static string TruncateForRow(string s)
            {
                string single = s.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                if (single.Length > PreviewMaxChars) single = single.Substring(0, PreviewMaxChars) + "…";
                return single;
            }

            private static string RelativeTime(DateTime then)
            {
                var span = DateTime.Now - then;
                if (span.TotalSeconds < 60) return "刚刚";
                if (span.TotalMinutes < 60) return ((int)span.TotalMinutes) + " 分钟前";
                if (span.TotalHours < 24) return ((int)span.TotalHours) + " 小时前";
                if (span.TotalDays < 7) return ((int)span.TotalDays) + " 天前";
                return then.ToString("MM/dd");
            }
        }
    }
}
