using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pastix.UI
{
    /// <summary>
    /// 自绘图标按钮：圆角、无边框、悬停/按下/选中三态。
    /// </summary>
    internal sealed class IconButton : Control
    {
        public Icons.IconKind Icon { get; set; }
        public Color? AccentBackground { get; set; } // 非空则始终使用此底色（用于绿✓红✕）
        public bool IsActive { get; set; }
        public string Tooltip { get; set; }

        private bool _hover;
        private bool _pressed;

        public IconButton()
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Hand;
            this.Size = new Size(30, 30);
            this.TabStop = false;
        }

        public void SetActive(bool active)
        {
            if (IsActive == active) return;
            IsActive = active;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = new RectangleF(1, 1, Width - 2, Height - 2);

            // 背景
            Color bg;
            if (AccentBackground.HasValue)
                bg = _hover ? Lighten(AccentBackground.Value, 0.12f) : AccentBackground.Value;
            else if (IsActive)
                bg = Theme.Accent;
            else if (_pressed)
                bg = Theme.ButtonPressed;
            else if (_hover)
                bg = Theme.ButtonHover;
            else
                bg = Theme.ButtonIdle;

            if (bg.A > 0)
            {
                using (var path = GraphicsHelpers.RoundRect(rect, 6f))
                using (var brush = new SolidBrush(bg))
                    g.FillPath(brush, path);
            }

            // 图标颜色
            Color iconColor = (IsActive || AccentBackground.HasValue)
                ? Theme.IconColorActive
                : (_hover ? Theme.IconColorHover : Theme.IconColor);

            Icons.Draw(g, Icon, rect, iconColor, 1.8f);
        }

        private static Color Lighten(Color c, float amount)
        {
            int r = (int)System.Math.Min(255, c.R + 255 * amount);
            int gC = (int)System.Math.Min(255, c.G + 255 * amount);
            int b = (int)System.Math.Min(255, c.B + 255 * amount);
            return Color.FromArgb(c.A, r, gC, b);
        }
    }
}

