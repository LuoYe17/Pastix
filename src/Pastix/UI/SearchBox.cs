using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pastix.UI
{
    /// <summary>
    /// 历史窗口顶部的搜索输入框：圆角实色背景、无边框、左侧放大镜图标、占位文字。
    /// 聚焦时只让放大镜变 Accent，整体外观不改变。
    /// </summary>
    internal sealed class SearchBox : Panel
    {
        private const int CornerRadius = 6;
        private const int IconSize = 16;
        private const int IconLeftPad = 10;
        private const int IconTextGap = 8;
        private const int RightPad = 10;

        private static readonly Color FillColor = Color.FromArgb(255, 50, 50, 52);
        private static readonly Color PlaceholderColor = Color.FromArgb(140, 255, 255, 255);
        private static readonly Color IconIdle = Color.FromArgb(170, 255, 255, 255);

        private readonly TextBox _inner;
        private string _placeholder = string.Empty;

        public SearchBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(40, 40, 42);

            _inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = FillColor,
                ForeColor = Theme.IconColorActive,
                Font = Theme.UiFont(9.5f),
            };
            _inner.GotFocus += (s, e) => Invalidate();
            _inner.LostFocus += (s, e) => Invalidate();
            _inner.TextChanged += (s, e) => Invalidate(); // 占位文字显隐
            Controls.Add(_inner);
        }

        public TextBox Inner => _inner;

        public string PlaceholderText
        {
            get { return _placeholder; }
            set { _placeholder = value ?? string.Empty; Invalidate(); }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            int innerLeft = IconLeftPad + IconSize + IconTextGap;
            int innerH = _inner.PreferredHeight;
            _inner.SetBounds(innerLeft, Math.Max(0, (Height - innerH) / 2),
                Math.Max(0, Width - innerLeft - RightPad), innerH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (var path = GraphicsHelpers.RoundRect(new RectangleF(0, 0, Width, Height), CornerRadius))
            using (var brush = new SolidBrush(FillColor))
                g.FillPath(brush, path);

            var iconRect = new RectangleF(IconLeftPad, (Height - IconSize) / 2f, IconSize, IconSize);
            Icons.Draw(g, Icons.IconKind.Search, iconRect,
                _inner.Focused ? Theme.Accent : IconIdle, 1.6f);

            // 占位文字（仅在内容为空且未聚焦时一直显示；聚焦时仍显示直到用户输入）
            if (string.IsNullOrEmpty(_inner.Text) && !string.IsNullOrEmpty(_placeholder))
            {
                int textX = IconLeftPad + IconSize + IconTextGap;
                var textRect = new Rectangle(textX, 0, Width - textX - RightPad, Height);
                TextRenderer.DrawText(g, _placeholder, _inner.Font, textRect, PlaceholderColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
