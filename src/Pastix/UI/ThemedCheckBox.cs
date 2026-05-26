using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pastix.UI
{
    /// <summary>
    /// 设置面板用的圆角复选框：左侧 16x16 圆角方框 + 选中时 Accent 填充 + Icons.Check 对勾，右侧文字。
    /// </summary>
    internal sealed class ThemedCheckBox : Control
    {
        private const int BoxSize = 16;
        private const int BoxRadius = 4;
        private const int Gap = 8;

        private bool _checked;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public ThemedCheckBox()
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Hand;
            this.Font = Theme.UiFont(9.5f);
            this.ForeColor = Theme.IconColorActive;
            this.Size = new Size(220, 24);
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
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
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            this.Checked = !this.Checked;
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
                this.Checked = !this.Checked;
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int boxY = (Height - BoxSize) / 2;
            var boxRect = new RectangleF(0.5f, boxY + 0.5f, BoxSize - 1, BoxSize - 1);

            using (var path = GraphicsHelpers.RoundRect(boxRect, BoxRadius))
            {
                if (_checked)
                {
                    using (var brush = new SolidBrush(Theme.Accent))
                        g.FillPath(brush, path);
                }
                else
                {
                    Color hoverFill = _hover ? Theme.ButtonHover : Color.FromArgb(255, 28, 28, 30);
                    using (var brush = new SolidBrush(hoverFill))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f))
                        g.DrawPath(pen, path);
                }
            }

            if (_checked)
            {
                // 复用 Icons.Check 描线，stroke 加粗一点便于在 16px 内清晰
                Icons.Draw(g, Icons.IconKind.Check,
                    new RectangleF(0, boxY, BoxSize, BoxSize),
                    Theme.IconColorActive, 2.2f);
            }

            // 文本
            int textX = BoxSize + Gap;
            var textRect = new Rectangle(textX, 0, Math.Max(0, Width - textX), Height);
            TextRenderer.DrawText(g, Text ?? string.Empty, Font, textRect,
                this.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            if (this.Focused && this.TabStop)
            {
                ControlPaint.DrawFocusRectangle(g, textRect);
            }
        }
    }
}
