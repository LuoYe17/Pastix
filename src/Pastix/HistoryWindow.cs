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
        public event Action<ClipboardItem> ItemChosen;
        public event Action Cancelled;
        public event Action<ClipboardItem> ItemPinToggleRequested;
        public event Action<ClipboardItem> ItemRemoveRequested;

        private const int CardWidth = 400;
        private const int CornerRadius = 12;
        private const int Pad = 12;
        private const int SearchHeight = 32;
        private const int RowHeightText = 36;
        private const int RowHeightImage = 80;
        private const int StatusHeight = 28;
        private const int ListMaxHeight = 400;
        private const int ListEmptyHeight = 80;
        private const int PreviewMaxChars = 80;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private static int RowHeightOf(ClipboardItem it) =>
            it.Type == ClipboardItemType.Image ? RowHeightImage : RowHeightText;

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
                Size = new Size(CardWidth - Pad * 2, RowHeightText),
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
            ItemChosen?.Invoke(_filtered[idx]);
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
            bool filtering = !string.IsNullOrEmpty(q);
            _filtered.Clear();
            int pinned = 0;
            int textCount = 0;
            int imageCount = 0;
            foreach (var it in _all)
            {
                bool include;
                if (it.Type == ClipboardItemType.Image)
                {
                    // 图片永远保留，不参与文字过滤
                    include = true;
                }
                else
                {
                    include = !filtering ||
                        (it.Text != null && it.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                if (include)
                {
                    _filtered.Add(it);
                    if (it.Pinned) pinned++;
                    if (it.Type == ClipboardItemType.Image) imageCount++;
                    else textCount++;
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

            _status.SetCounts(_filtered.Count, _all.Count, textCount, imageCount, pinned, isFiltering: filtering);
            RelayoutAndResize();
        }

        private void RelayoutAndResize()
        {
            int contentH = 0;
            for (int i = 0; i < _filtered.Count; i++) contentH += RowHeightOf(_filtered[i]);
            int listH = _filtered.Count > 0
                ? Math.Min(contentH, ListMaxHeight)
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

            public void SetCounts(int shown, int total, int textCount, int imageCount, int pinned, bool isFiltering)
            {
                string countPart;
                if (isFiltering)
                {
                    countPart = string.Format("{0}/{1} 条", shown, total);
                    if (pinned > 0) countPart += string.Format("（{0} 置顶）", pinned);
                }
                else if (imageCount == 0)
                {
                    countPart = string.Format("{0} 条", total);
                    if (pinned > 0) countPart += string.Format("（{0} 置顶）", pinned);
                }
                else if (textCount == 0)
                {
                    countPart = string.Format("{0} 张图片", total);
                    if (pinned > 0) countPart += string.Format("（{0} 置顶）", pinned);
                }
                else
                {
                    countPart = string.Format("{0} 条（{1} 文本 · {2} 图片", total, textCount, imageCount);
                    countPart += pinned > 0 ? string.Format("，{0} 置顶）", pinned) : "）";
                }

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
            // 自绘滚动条颜色（白色不同 alpha）
            private static readonly Color ScrollThumbIdle = Color.FromArgb(60, 255, 255, 255);
            private static readonly Color ScrollThumbHover = Color.FromArgb(140, 255, 255, 255);
            private static readonly Color ScrollThumbDrag = Color.FromArgb(200, 255, 255, 255);
            private const int LeftTextPad = 12;
            private const int RightTextPad = 12;
            private const int AccentBarWidth = 3;
            private const int TimeMaxWidth = 80;
            private const int RowButtonSize = 30;
            private const int RowButtonGap = 2;
            private const int RowButtonIconInset = 5; // 30 - 5*2 = 20px 图标
            // 滚动条几何
            private const int ScrollThinWidth = 3;
            private const int ScrollThickWidth = 8;
            private const int ScrollRightPad = 2;
            private const int ScrollMinThumbHeight = 20;
            // 命中测试时排除 thumb 区域（避免在 thumb 上点中行）
            private const int ScrollHitReserve = ScrollThickWidth + ScrollRightPad;

            // 行内子区域（命中测试用）
            private enum HitZone { Row, PinButton, DeleteButton }

            private List<ClipboardItem> _items = new List<ClipboardItem>();
            private int _selected = -1;
            private int _hover = -1;
            private HitZone _hoverZone = HitZone.Row;
            private bool _hasNoSource;
            private int _pinnedCount;

            // 自绘滚动条状态
            private int _scrollY;
            private bool _isPanelHovered;
            private bool _isDraggingScroll;
            private int _scrollDragStartY;
            private int _scrollDragStartScrollY;

            public ListPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Color.FromArgb(40, 40, 42);
            }

            // 缩略图解码缓存：key = ClipboardItem 引用。SetItems 时清空。
            private readonly Dictionary<ClipboardItem, Image> _thumbCache =
                new Dictionary<ClipboardItem, Image>();

            public int SelectedIndex => _selected;

            public void SetItems(List<ClipboardItem> items, bool hasNoSource, int pinnedCount, int selected)
            {
                // 清理已不在列表中的缩略图缓存（按引用判断）
                if (_thumbCache.Count > 0)
                {
                    var alive = new HashSet<ClipboardItem>();
                    if (items != null)
                    {
                        foreach (var it in items)
                            if (it.Type == ClipboardItemType.Image) alive.Add(it);
                    }
                    var stale = new List<ClipboardItem>();
                    foreach (var kv in _thumbCache)
                        if (!alive.Contains(kv.Key)) stale.Add(kv.Key);
                    foreach (var key in stale)
                    {
                        _thumbCache[key]?.Dispose();
                        _thumbCache.Remove(key);
                    }
                }

                _items = items ?? new List<ClipboardItem>();
                _hasNoSource = hasNoSource;
                _pinnedCount = pinnedCount;
                _selected = (_items.Count == 0) ? -1
                    : Math.Max(0, Math.Min(_items.Count - 1, selected));
                _hover = -1;
                _hoverZone = HitZone.Row;
                _isDraggingScroll = false;
                ClampScroll();
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

            private int ContentHeight
            {
                get
                {
                    int h = 0;
                    for (int i = 0; i < _items.Count; i++) h += RowHeightOf(_items[i]);
                    return h;
                }
            }
            private bool IsScrollVisible => ContentHeight > Height && Height > 0;
            private int MaxScrollY => Math.Max(0, ContentHeight - Height);

            /// <summary>从 0 累加得到第 idx 行的 Y 起点（相对内容坐标）。</summary>
            private int RowTop(int idx)
            {
                int y = 0;
                for (int i = 0; i < idx; i++) y += RowHeightOf(_items[i]);
                return y;
            }

            private void ClampScroll()
            {
                int max = MaxScrollY;
                if (_scrollY < 0) _scrollY = 0;
                else if (_scrollY > max) _scrollY = max;
            }

            /// <summary>
            /// 计算 thumb 矩形（坐标在 ListPanel 内）。仅在 IsScrollVisible 时有效。
            /// </summary>
            private Rectangle GetThumbRect()
            {
                int width = _isPanelHovered || _isDraggingScroll ? ScrollThickWidth : ScrollThinWidth;
                int contentH = ContentHeight;
                int thumbH = Math.Max(ScrollMinThumbHeight, (int)((long)Height * Height / contentH));
                if (thumbH > Height) thumbH = Height;
                int max = MaxScrollY;
                int travel = Height - thumbH;
                int thumbY = max <= 0 ? 0 : (int)((long)_scrollY * travel / max);
                int x = Width - width - ScrollRightPad;
                return new Rectangle(x, thumbY, width, thumbH);
            }

            private void EnsureVisible(int idx)
            {
                if (idx < 0 || !IsScrollVisible) return;
                int top = RowTop(idx);
                int bottom = top + RowHeightOf(_items[idx]);
                if (top < _scrollY) _scrollY = top;
                else if (bottom > _scrollY + Height) _scrollY = bottom - Height;
                ClampScroll();
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                ClampScroll();
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (!IsScrollVisible) return;
                _scrollY -= (e.Delta / 120) * RowHeightText * 2;
                ClampScroll();
                Invalidate();
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                if (!_isPanelHovered)
                {
                    _isPanelHovered = true;
                    Invalidate();
                }
            }

            private int IndexAt(Point p)
            {
                // 滚动条 thumb 区域排除（避免在 thumb 上点中行）
                if (IsScrollVisible && p.X >= Width - ScrollHitReserve) return -1;
                int target = p.Y + _scrollY;
                if (target < 0) return -1;
                int y = 0;
                for (int i = 0; i < _items.Count; i++)
                {
                    int h = RowHeightOf(_items[i]);
                    if (target < y + h) return i;
                    y += h;
                }
                return -1;
            }

            /// <summary>
            /// 计算行内按钮区域。pin 按钮始终存在；delete 按钮仅 hover 时显示。
            /// </summary>
            private void GetRowButtonRects(Rectangle rowRect, out Rectangle pinBtn, out Rectangle deleteBtn)
            {
                int yMid = rowRect.Y + (rowRect.Height - RowButtonSize) / 2;
                int rightEdge = rowRect.Right - RightTextPad;
                pinBtn = new Rectangle(rightEdge - RowButtonSize, yMid, RowButtonSize, RowButtonSize);
                deleteBtn = new Rectangle(pinBtn.X - RowButtonGap - RowButtonSize, yMid, RowButtonSize, RowButtonSize);
            }

            private HitZone ZoneAt(int idx, Point p)
            {
                if (idx < 0 || idx >= _items.Count) return HitZone.Row;
                int rightPad = IsScrollVisible ? ScrollHitReserve : 0;
                int rowH = RowHeightOf(_items[idx]);
                int y = RowTop(idx) - _scrollY;
                var rowRect = new Rectangle(0, y, Width - rightPad, rowH);
                GetRowButtonRects(rowRect, out var pinBtn, out var delBtn);
                if (pinBtn.Contains(p)) return HitZone.PinButton;
                if (delBtn.Contains(p)) return HitZone.DeleteButton;
                return HitZone.Row;
            }

            private bool IsOverThumb(Point p)
            {
                if (!IsScrollVisible) return false;
                return GetThumbRect().Contains(p);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);

                // 拖动 thumb 中：根据鼠标 Y 偏移更新 scrollY
                if (_isDraggingScroll)
                {
                    int thumbH = GetThumbRect().Height;
                    int travel = Math.Max(1, Height - thumbH);
                    int max = MaxScrollY;
                    int dy = e.Y - _scrollDragStartY;
                    long newScroll = (long)_scrollDragStartScrollY + (long)dy * max / travel;
                    _scrollY = (int)Math.Max(0, Math.Min(max, newScroll));
                    Invalidate();
                    return;
                }

                int h = IndexAt(e.Location);
                var z = ZoneAt(h, e.Location);
                bool overThumb = IsOverThumb(e.Location);
                if (h != _hover || z != _hoverZone)
                {
                    _hover = h;
                    _hoverZone = z;
                    Invalidate();
                }
                // 在按钮区或 thumb 上显示手型
                Cursor = (z == HitZone.PinButton || z == HitZone.DeleteButton || overThumb)
                    ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _isPanelHovered = false;
                if (_hover != -1 || _hoverZone != HitZone.Row)
                {
                    _hover = -1;
                    _hoverZone = HitZone.Row;
                }
                Cursor = Cursors.Default;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                if (IsOverThumb(e.Location))
                {
                    _isDraggingScroll = true;
                    _scrollDragStartY = e.Y;
                    _scrollDragStartScrollY = _scrollY;
                    Capture = true;
                    Invalidate();
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (_isDraggingScroll)
                {
                    _isDraggingScroll = false;
                    Capture = false;
                    Invalidate();
                }
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                // 滚动条拖动结束时 OnMouseUp 已处理；点击 thumb 不触发列表行
                if (IsOverThumb(e.Location)) return;
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

                // 自绘滚动条不占据布局空间，但命中测试预留 ScrollHitReserve 防止 thumb 上误点
                int rightPad = IsScrollVisible ? ScrollHitReserve : 0;

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

                int viewTop = _scrollY;
                int viewBottom = viewTop + Height;

                // 找首个可见行：累加直到底超过 viewTop
                int firstIdx = 0;
                int firstTop = 0;
                {
                    int yAcc = 0;
                    for (int i = 0; i < _items.Count; i++)
                    {
                        int h = RowHeightOf(_items[i]);
                        if (yAcc + h > viewTop) { firstIdx = i; firstTop = yAcc; break; }
                        yAcc += h;
                        firstIdx = i + 1;
                        firstTop = yAcc;
                    }
                }

                using (var font = Theme.UiFont(9.5f))
                using (var timeFont = Theme.UiFont(8.5f))
                using (var imgInfoFont = Theme.UiFont(9f))
                {
                    int rowTopAbs = firstTop;
                    int sepY = -1;
                    for (int i = firstIdx; i < _items.Count; i++)
                    {
                        if (rowTopAbs >= viewBottom) break;
                        int rowH = RowHeightOf(_items[i]);
                        int y = rowTopAbs - viewTop;
                        var rowRect = new Rectangle(0, y, Width - rightPad, rowH);

                        bool isSelected = (i == _selected);
                        bool isHover = (i == _hover);
                        var item = _items[i];

                        if (isSelected)
                        {
                            using (var brush = new SolidBrush(SelectedOverlay))
                                g.FillRectangle(brush, rowRect);
                            using (var bar = new SolidBrush(RowAccentBar))
                                g.FillRectangle(bar, rowRect.X, rowRect.Y, AccentBarWidth, rowRect.Height);
                        }
                        if (isHover)
                        {
                            using (var brush = new SolidBrush(HoverOverlay))
                                g.FillRectangle(brush, rowRect);
                        }

                        // 右侧按钮/时间布局规则与文本行一致
                        bool showDeleteBtn = isHover;
                        bool showTime = !item.Pinned && !isHover;

                        int rightUsedW;
                        int timeW = 0;
                        if (showDeleteBtn)
                        {
                            rightUsedW = RowButtonSize * 2 + RowButtonGap;
                        }
                        else if (showTime)
                        {
                            timeW = TextRenderer.MeasureText(RelativeTime(item.CapturedAt), timeFont).Width;
                            if (timeW > TimeMaxWidth) timeW = TimeMaxWidth;
                            rightUsedW = RowButtonSize + RowButtonGap + timeW;
                        }
                        else
                        {
                            rightUsedW = RowButtonSize;
                        }

                        if (item.Type == ClipboardItemType.Image)
                        {
                            DrawImageRow(g, rowRect, item, font, imgInfoFont, timeFont,
                                isHover, rightUsedW, timeW);
                        }
                        else
                        {
                            string preview = TruncateForRow(item.Text);
                            var mainRect = new Rectangle(
                                rowRect.X + LeftTextPad,
                                rowRect.Y,
                                Math.Max(0, rowRect.Width - LeftTextPad - RightTextPad - rightUsedW - 8),
                                rowRect.Height);
                            TextRenderer.DrawText(g, preview, font, mainRect, Theme.IconColorActive,
                                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                        }

                        GetRowButtonRects(rowRect, out var pinBtn, out var delBtn);
                        DrawPinButton(g, pinBtn, item.Pinned, _hoverZone == HitZone.PinButton && isHover);

                        if (showDeleteBtn)
                        {
                            DrawRowButton(g, delBtn, Icons.IconKind.Trash,
                                _hoverZone == HitZone.DeleteButton && isHover);
                        }
                        else if (showTime && item.Type == ClipboardItemType.Text)
                        {
                            string timeText = RelativeTime(item.CapturedAt);
                            var timeRect = new Rectangle(
                                pinBtn.X - RowButtonGap - timeW,
                                rowRect.Y,
                                timeW,
                                rowRect.Height);
                            TextRenderer.DrawText(g, timeText, timeFont, timeRect, SubTextColor,
                                TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                                TextFormatFlags.NoPrefix);
                        }

                        rowTopAbs += rowH;

                        // 记录 pinned/unpinned 分隔线位置（在第 _pinnedCount 行的顶端）
                        if (_pinnedCount > 0 && _pinnedCount < _items.Count && i + 1 == _pinnedCount)
                            sepY = rowTopAbs - viewTop;
                    }

                    // pinned/unpinned 分隔线（_pinnedCount 行可能不在可见范围；按整列累加更稳妥）
                    if (sepY < 0 && _pinnedCount > 0 && _pinnedCount < _items.Count)
                    {
                        int sepAbs = 0;
                        for (int k = 0; k < _pinnedCount; k++) sepAbs += RowHeightOf(_items[k]);
                        sepY = sepAbs - viewTop;
                    }
                    if (sepY >= 0 && sepY <= Height)
                    {
                        using (var pen = new Pen(GroupSeparator, 1f))
                            g.DrawLine(pen, 0, sepY, Width - rightPad, sepY);
                    }
                }

                // 自绘极细滚动条（在所有内容之上）
                if (IsScrollVisible)
                {
                    Color thumbColor = _isDraggingScroll
                        ? ScrollThumbDrag
                        : (_isPanelHovered ? ScrollThumbHover : ScrollThumbIdle);
                    var thumb = GetThumbRect();
                    using (var brush = new SolidBrush(thumbColor))
                    using (var path = GraphicsHelpers.RoundRect(thumb, thumb.Width / 2f))
                        g.FillPath(brush, path);
                }
            }

            private static void DrawPinButton(Graphics g, Rectangle rect, bool pinned, bool hovered)
            {
                if (hovered)
                {
                    using (var path = GraphicsHelpers.RoundRect(rect, 6))
                    using (var brush = new SolidBrush(RowButtonHover))
                        g.FillPath(brush, path);
                }
                // 30×30 容器内缩 5px → 20×20 图标
                var iconRect = new RectangleF(
                    rect.X + RowButtonIconInset,
                    rect.Y + RowButtonIconInset,
                    rect.Width - RowButtonIconInset * 2,
                    rect.Height - RowButtonIconInset * 2);
                if (pinned)
                {
                    Icons.Draw(g, Icons.IconKind.PinFilled, iconRect, Theme.Accent, strokeWidth: 1.6f);
                }
                else
                {
                    Icons.Draw(g, Icons.IconKind.Pin, iconRect, Theme.IconColor, strokeWidth: 1.6f);
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
                // 30×30 容器内缩 5px → 20×20 图标
                var iconRect = new RectangleF(
                    rect.X + RowButtonIconInset,
                    rect.Y + RowButtonIconInset,
                    rect.Width - RowButtonIconInset * 2,
                    rect.Height - RowButtonIconInset * 2);
                Icons.Draw(g, icon, iconRect, Theme.IconColor, strokeWidth: 1.6f);
            }

            // ---------------- 图片行渲染 ----------------

            private const int ImageRowVPad = 12;        // 上下内边距
            private const int ImageThumbBoxW = 88;
            private const int ImageThumbBoxH = 56;      // 80 - 12*2 = 56
            private const int ImageThumbLeftPad = 12;
            private const int ImageInfoLeftPad = 12;    // 缩略图右侧到文字的间隔

            /// <summary>
            /// 解码并缓存条目的缩略图。失败返回 null。
            /// </summary>
            private Image GetThumbnail(ClipboardItem item)
            {
                if (_thumbCache.TryGetValue(item, out var cached)) return cached;
                if (item.ThumbnailBytes == null || item.ThumbnailBytes.Length == 0) return null;
                Image img;
                try
                {
                    // Image.FromStream 要求 stream 在 Image 生命周期内保持可用，
                    // 所以加载后立即拷贝到一个独立的 Bitmap，避免 stream dispose 后的 GDI+ 异常。
                    using (var ms = new System.IO.MemoryStream(item.ThumbnailBytes))
                    using (var loaded = Image.FromStream(ms))
                    {
                        img = new Bitmap(loaded);
                    }
                }
                catch
                {
                    return null;
                }
                _thumbCache[item] = img;
                return img;
            }

            private void DrawImageRow(
                Graphics g, Rectangle rowRect, ClipboardItem item,
                Font preferredFont, Font infoFont, Font timeFont,
                bool isHover, int rightUsedW, int timeW)
            {
                // 缩略图区域：左 12 px、垂直居中、最大 88×56
                int thumbX = rowRect.X + ImageThumbLeftPad;
                int thumbY = rowRect.Y + (rowRect.Height - ImageThumbBoxH) / 2;
                var thumbBox = new Rectangle(thumbX, thumbY, ImageThumbBoxW, ImageThumbBoxH);

                var thumb = GetThumbnail(item);
                if (thumb != null)
                {
                    // 等比缩放，居中绘制
                    double scale = Math.Min(
                        (double)ImageThumbBoxW / thumb.Width,
                        (double)ImageThumbBoxH / thumb.Height);
                    if (scale > 1) scale = 1;
                    int dw = Math.Max(1, (int)(thumb.Width * scale));
                    int dh = Math.Max(1, (int)(thumb.Height * scale));
                    int dx = thumbBox.X + (thumbBox.Width - dw) / 2;
                    int dy = thumbBox.Y + (thumbBox.Height - dh) / 2;
                    var oldInterp = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(thumb, new Rectangle(dx, dy, dw, dh));
                    g.InterpolationMode = oldInterp;
                }
                else
                {
                    // 缩略图缺失时画占位
                    using (var pen = new Pen(GroupSeparator, 1f))
                        g.DrawRectangle(pen, thumbBox);
                }

                // 中间信息：图片 · 1920×1080 · 245 KB（hover/pin 状态把时间塞进去）
                int infoLeft = thumbBox.Right + ImageInfoLeftPad;
                int infoRight = rowRect.Right - RightTextPad - rightUsedW - 8;
                int infoWidth = Math.Max(0, infoRight - infoLeft);
                if (infoWidth <= 0) return;

                string info = BuildImageInfo(item, includeTime: !isHover && !item.Pinned);
                var infoRect = new Rectangle(infoLeft, rowRect.Y, infoWidth, rowRect.Height);
                TextRenderer.DrawText(g, info, infoFont, infoRect, Theme.IconColorActive,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                // 抑制 unused 警告（当前实现不直接使用 preferredFont/timeFont/timeW，但保留接口便于将来扩展）
                _ = preferredFont; _ = timeFont; _ = timeW;
            }

            private static string BuildImageInfo(ClipboardItem item, bool includeTime)
            {
                int kb = ((item.ImageBytes != null ? item.ImageBytes.Length : 0) + 1023) / 1024;
                string size = (item.ImageWidth > 0 && item.ImageHeight > 0)
                    ? string.Format("{0}×{1}", item.ImageWidth, item.ImageHeight)
                    : null;
                string sizeKB = kb > 0 ? string.Format("{0} KB", kb) : null;

                var parts = new System.Collections.Generic.List<string>(4) { "图片" };
                if (size != null) parts.Add(size);
                if (sizeKB != null) parts.Add(sizeKB);
                if (includeTime) parts.Add(RelativeTime(item.CapturedAt));
                return string.Join(" · ", parts);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    foreach (var kv in _thumbCache) kv.Value?.Dispose();
                    _thumbCache.Clear();
                }
                base.Dispose(disposing);
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
