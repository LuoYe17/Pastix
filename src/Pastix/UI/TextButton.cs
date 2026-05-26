using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pastix.UI
{
    /// <summary>
    /// 设置面板用的圆角文本按钮：两种样式 Accent（蓝底白字）和 Ghost（透明底白字）。
    /// 实现 IButtonControl 以便作为 Form.AcceptButton / CancelButton 使用。
    /// </summary>
    internal sealed class TextButton : Control, IButtonControl
    {
        public enum ButtonStyle
        {
            Accent,
            Ghost,
        }

        private const int CornerRadius = 6;

        private bool _hover;
        private bool _pressed;
        private DialogResult _dialogResult = DialogResult.None;

        public ButtonStyle Style { get; set; } = ButtonStyle.Ghost;

        public TextButton()
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Hand;
            this.Size = new Size(80, 30);
            this.Font = Theme.UiFont(9.5f);
            this.ForeColor = Theme.IconColorActive;
        }

        public DialogResult DialogResult
        {
            get { return _dialogResult; }
            set { _dialogResult = value; }
        }

        public void NotifyDefault(bool value)
        {
            // 默认按钮高亮已经由 Style=Accent 表达，无需额外视觉
        }

        public void PerformClick()
        {
            if (this.CanSelect) OnClick(EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && _pressed)
            {
                _pressed = false;
                Invalidate();
                PerformClick();
            }
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
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                this.Focus();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_pressed)
            {
                _pressed = false;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = GraphicsHelpers.RoundRect(rect, CornerRadius))
            {
                Color fill = ResolveBackgroundColor();
                if (fill.A > 0)
                {
                    using (var brush = new SolidBrush(fill))
                        g.FillPath(brush, path);
                }

                if (Style == ButtonStyle.Ghost)
                {
                    using (var pen = new Pen(Theme.Divider, 1f))
                        g.DrawPath(pen, path);
                }

                if (this.Focused && this.TabStop)
                {
                    var inner = new RectangleF(rect.X + 1.5f, rect.Y + 1.5f, rect.Width - 3, rect.Height - 3);
                    using (var focusPath = GraphicsHelpers.RoundRect(inner, CornerRadius - 1))
                    using (var pen = new Pen(Color.FromArgb(140, 255, 255, 255), 1f))
                        g.DrawPath(pen, focusPath);
                }
            }

            // 文字
            var textRect = new Rectangle(0, 0, Width, Height);
            TextRenderer.DrawText(g, Text ?? string.Empty, Font, textRect,
                this.Enabled ? Theme.IconColorActive : Color.FromArgb(120, 255, 255, 255),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        private Color ResolveBackgroundColor()
        {
            if (Style == ButtonStyle.Accent)
            {
                if (!Enabled) return Color.FromArgb(120, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B);
                if (_pressed) return Lighten(Theme.Accent, -0.10f);
                if (_hover) return Lighten(Theme.Accent, 0.10f);
                return Theme.Accent;
            }
            else // Ghost
            {
                if (_pressed) return Theme.ButtonPressed;
                if (_hover) return Theme.ButtonHover;
                return Theme.ButtonIdle;
            }
        }

        private static Color Lighten(Color c, float amount)
        {
            int r = Clamp((int)(c.R + 255 * amount));
            int g = Clamp((int)(c.G + 255 * amount));
            int b = Clamp((int)(c.B + 255 * amount));
            return Color.FromArgb(c.A, r, g, b);
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }
    }
}
