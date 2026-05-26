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
    /// 行 hover 时右侧出现图钉/删除按钮；P 切换 pin、Delete 移除选中行。
    /// </summary>
    internal sealed class HistoryWindow : Form
    {
        public event Action<string> ItemChosen;
        public event Action Cancelled;
        public event Action<ClipboardItem> ItemPinToggleRequested;
        public event Action<ClipboardItem> ItemRemoveRequested;

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
        // 数据变更刷新后用于恢复选中：若设置则优先按引用查找，否则按索引 clamp
        private ClipboardItem _followItem;
        private int _followIndexFallback = -1;

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
            _list.ItemPinToggleRequested += idx => RequestPinToggle(idx);
            _list.ItemRemoveRequested += idx => RequestRemove(idx);
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
            _followItem = null;
            _followIndexFallback = -1;
            ApplyFilter(); // 内部会 RelayoutAndPosition

            var screen = Screen.FromPoint(cursor).WorkingArea;
            Location = new Point(
                screen.Left + (screen.Width - Width) / 2,
                screen.Top + (screen.Height - Height) / 2);

            Show();
            Activate();
            _search.Inner.Focus();
        }

        /// <summary>
        /// 数据源变更后由外部（Program）调用，刷新当前列表并尽可能保持选中。
        /// </summary>
        public void RefreshItems(IReadOnlyList<ClipboardItem> items)
        {
            _all = items ?? Array.Empty<ClipboardItem>();
            ApplyFilter();
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
            // 提取裸键码用于 P 这种允许 Shift 大小写变体的快捷键
            Keys code = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;

            switch (keyData)
            {
                case Keys.Escape:
                    _suppressDeactivate = true; Cancelled?.Invoke(); return true;
                case Keys.Down:
                    _list.MoveSelection(1); return true;
                case Keys.Up:
                    _list.MoveSelection(-1); return true;
                case Keys.Enter:
                    Choose(_list.SelectedIndex); return true;
                case Keys.Delete:
                    // 搜索框聚焦但有文字 → 走 TextBox 默认行为（删字符）
                    if (_search.Inner.Focused && _search.Inner.TextLength > 0)
                        return base.ProcessCmdKey(ref msg, keyData);
                    RequestRemove(_list.SelectedIndex);
                    return true;
            }

            // P 或 Shift+P：仅当搜索框未聚焦时拦截，否则让其作为字母输入到搜索框
            if (code == Keys.P && (mods == Keys.None || mods == Keys.Shift))
            {
                if (!_search.Inner.Focused)
                {
                    RequestPinToggle(_list.SelectedIndex);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Choose(int idx)
        {
            if (idx < 0 || idx >= _filtered.Count) return;
            _suppressDeactivate = true;
            ItemChosen?.Invoke(_filtered[idx].Text);
        }

        private void RequestPinToggle(int idx)
        {
            if (idx < 0 || idx >= _filtered.Count) return;
            var item = _filtered[idx];
            // 切换后该条目位置会变（pinned 跳到顶部分组、unpinned 回到时间倒序位置）
            // 通过 _followItem 让刷新后选中跟随该 item
            _followItem = item;
            _followIndexFallback = idx;
            ItemPinToggleRequested?.Invoke(item);
        }

        private void RequestRemove(int idx)
        {
            if (idx < 0 || idx >= _filtered.Count) return;
            var item = _filtered[idx];
            // 删除后选中应落在原位置（自动后移一行；若超出则 clamp 到最后一行）
            _followItem = null;
            _followIndexFallback = idx;
            ItemRemoveRequested?.Invoke(item);
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
            int pinned = 0;
            foreach (var it in _all)
            {
                if (string.IsNullOrEmpty(q) ||
                    it.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _filtered.Add(it);
                    if (it.Pinned) pinned++;
                }
            }

            // 计算刷新后的选中行
            int newSelected = _filtered.Count > 0 ? 0 : -1;
            if (_filtered.Count > 0)
            {
                if (_followItem != null)
                {
                    int found = -1;
                    for (int i = 0; i < _filtered.Count; i++)
                        if (ReferenceEquals(_filtered[i], _followItem)) { found = i; break; }
                    if (found >= 0) newSelected = found;
                    else if (_followIndexFallback >= 0)
                        newSelected = Math.Min(_followIndexFallback, _filtered.Count - 1);
                }
                else if (_followIndexFallback >= 0)
                {
                    newSelected = Math.Min(_followIndexFallback, _filtered.Count - 1);
                }
            }
            _followItem = null;
            _followIndexFallback = -1;

            _list.SetItems(_filtered, hasNoSource: _all.Count == 0, pinnedCount: pinned, selected: newSelected);

            // 状态栏：搜索态保留 shown/total；非搜索态显示 总数（置顶数）
            _status.SetCounts(_filtered.Count, _all.Count, pinned, isFiltering: !string.IsNullOrEmpty(q));
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

            public void SetCounts(int shown, int total, int pinned, bool isFiltering)
            {
                string countPart = isFiltering
                    ? string.Format("{0}/{1} 条", shown, total)
                    : string.Format("{0} 条", total);
                if (pinned > 0)
                    countPart += string.Format("（{0} 置顶）", pinned);

                _text = countPart + " · ↑↓ 选择 · Enter 粘贴 · P 置顶 · Del 删除 · Esc 关闭";
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
            public event Action<int> ItemPinToggleRequested;
            public event Action<int> ItemRemoveRequested;

            private static readonly Color HoverOverlay = Color.FromArgb(20, 255, 255, 255);
            private static readonly Color SelectedOverlay = Color.FromArgb(45, 10, 132, 255);
            private static readonly Color RowAccentBar = Theme.Accent;
            private static readonly Color SubTextColor = Color.FromArgb(160, 255, 255, 255);
            private static readonly Color EmptyTextColor = Color.FromArgb(140, 255, 255, 255);
            private static readonly Color GroupSeparator = Color.FromArgb(40, 255, 255, 255);
            private static readonly Color RowButtonHover = Color.FromArgb(45, 255, 255, 255);
            private const int LeftTextPad = 12;
            private const int RightTextPad = 12;
            private const int AccentBarWidth = 3;
            private const int TimeMaxWidth = 80;
            private const int RowButtonSize = 28;
            private const int RowButtonGap = 2;
            private const int PinIndicatorSize = 16;

            // 行内子区域（命中测试用）
            private enum HitZone { Row, PinButton, DeleteButton }

            private readonly VScrollBar _scroll;
            private List<ClipboardItem> _items = new List<ClipboardItem>();
            private int _selected = -1;
            private int _hover = -1;
            private HitZone _hoverZone = HitZone.Row;
            private bool _hasNoSource;
            private int _pinnedCount;

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

            public void SetItems(List<ClipboardItem> items, bool hasNoSource, int pinnedCount, int selected)
            {
                _items = items ?? new List<ClipboardItem>();
                _hasNoSource = hasNoSource;
                _pinnedCount = pinnedCount;
                _selected = (_items.Count == 0) ? -1
                    : Math.Max(0, Math.Min(_items.Count - 1, selected));
                _hover = -1;
                _hoverZone = HitZone.Row;
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

            /// <summary>
            /// 计算未 pin 行 hover 时的两个按钮区域。返回值是按钮的 (pinRect, deleteRect)。
            /// 未 hover 或 pinned 行返回 Rectangle.Empty。
            /// </summary>
            private void GetRowButtonRects(Rectangle rowRect, out Rectangle pinBtn, out Rectangle deleteBtn)
            {
                int yMid = rowRect.Y + (rowRect.Height - RowButtonSize) / 2;
                int rightEdge = rowRect.Right - RightTextPad;
                deleteBtn = new Rectangle(rightEdge - RowButtonSize, yMid, RowButtonSize, RowButtonSize);
                pinBtn = new Rectangle(deleteBtn.X - RowButtonGap - RowButtonSize, yMid, RowButtonSize, RowButtonSize);
            }

            private HitZone ZoneAt(int idx, Point p)
            {
                if (idx < 0 || idx >= _items.Count) return HitZone.Row;
                if (_items[idx].Pinned) return HitZone.Row; // pinned 行不显示按钮
                int rightPad = _scroll.Visible ? _scroll.Width : 0;
                int y = idx * RowHeight - _scroll.Value;
                var rowRect = new Rectangle(0, y, Width - rightPad, RowHeight);
                GetRowButtonRects(rowRect, out var pinBtn, out var delBtn);
                if (pinBtn.Contains(p)) return HitZone.PinButton;
                if (delBtn.Contains(p)) return HitZone.DeleteButton;
                return HitZone.Row;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = IndexAt(e.Location);
                var z = ZoneAt(h, e.Location);
                if (h != _hover || z != _hoverZone)
                {
                    _hover = h;
                    _hoverZone = z;
                    // 在按钮区显示手型，提示可点击
                    Cursor = (z == HitZone.PinButton || z == HitZone.DeleteButton)
                        ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_hover != -1 || _hoverZone != HitZone.Row)
                {
                    _hover = -1;
                    _hoverZone = HitZone.Row;
                    Cursor = Cursors.Default;
                    Invalidate();
                }
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                int idx = IndexAt(e.Location);
                if (idx < 0) return;
                _selected = idx;
                Invalidate();

                var zone = ZoneAt(idx, e.Location);
                switch (zone)
                {
                    case HitZone.PinButton:
                        ItemPinToggleRequested?.Invoke(idx);
                        break;
                    case HitZone.DeleteButton:
                        ItemRemoveRequested?.Invoke(idx);
                        break;
                    default:
                        ItemActivated?.Invoke(idx);
                        break;
                }
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
                        var item = _items[i];

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

                        // 计算右侧占位宽度（决定主文本可用宽）
                        bool showRowButtons = isHover && !item.Pinned;
                        int rightUsedW;
                        if (item.Pinned)
                        {
                            rightUsedW = PinIndicatorSize; // 实心图钉指示
                        }
                        else if (showRowButtons)
                        {
                            rightUsedW = RowButtonSize * 2 + RowButtonGap;
                        }
                        else
                        {
                            int timeW = TextRenderer.MeasureText(RelativeTime(item.CapturedAt), timeFont).Width;
                            if (timeW > TimeMaxWidth) timeW = TimeMaxWidth;
                            rightUsedW = timeW;
                        }

                        // 主文本
                        string preview = TruncateForRow(item.Text);
                        var mainRect = new Rectangle(
                            rowRect.X + LeftTextPad,
                            rowRect.Y,
                            Math.Max(0, rowRect.Width - LeftTextPad - RightTextPad - rightUsedW - 8),
                            rowRect.Height);
                        TextRenderer.DrawText(g, preview, font, mainRect, Theme.IconColorActive,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                        // 右侧渲染：pinned 指示 / 行按钮 / 时间
                        if (item.Pinned)
                        {
                            int iy = rowRect.Y + (rowRect.Height - PinIndicatorSize) / 2;
                            int ix = rowRect.Right - RightTextPad - PinIndicatorSize;
                            Icons.Draw(g, Icons.IconKind.PinFilled,
                                new RectangleF(ix, iy, PinIndicatorSize, PinIndicatorSize),
                                Theme.Accent, strokeWidth: 1.6f);
                        }
                        else if (showRowButtons)
                        {
                            GetRowButtonRects(rowRect, out var pinBtn, out var delBtn);
                            DrawRowButton(g, pinBtn, Icons.IconKind.Pin,
                                _hoverZone == HitZone.PinButton);
                            DrawRowButton(g, delBtn, Icons.IconKind.Trash,
                                _hoverZone == HitZone.DeleteButton);
                        }
                        else
                        {
                            string timeText = RelativeTime(item.CapturedAt);
                            int timeW = TextRenderer.MeasureText(timeText, timeFont).Width;
                            if (timeW > TimeMaxWidth) timeW = TimeMaxWidth;
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

                    // pinned 与未 pinned 之间的分组分隔线（在所有可见行之上画）
                    if (_pinnedCount > 0 && _pinnedCount < _items.Count)
                    {
                        int sepY = _pinnedCount * RowHeight - viewTop;
                        if (sepY >= 0 && sepY <= Height)
                        {
                            using (var pen = new Pen(GroupSeparator, 1f))
                                g.DrawLine(pen, 0, sepY, Width - rightPad, sepY);
                        }
                    }
                }
            }

            private static void DrawRowButton(Graphics g, Rectangle rect, Icons.IconKind icon, bool hovered)
            {
                if (hovered)
                {
                    using (var path = GraphicsHelpers.RoundRect(rect, 6))
                    using (var brush = new SolidBrush(RowButtonHover))
                        g.FillPath(brush, path);
                }
                // 图标内缩 6px，在 28×28 中得到 16×16 视觉
                var iconRect = new RectangleF(rect.X + 6, rect.Y + 6, rect.Width - 12, rect.Height - 12);
                Icons.Draw(g, icon, iconRect, Theme.IconColor, strokeWidth: 1.6f);
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
