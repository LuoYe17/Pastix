using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pastix.UI
{
    /// <summary>
    /// 设置面板专用的圆角输入框：一个 Panel 容器画圆角深色背景，内嵌无边框 TextBox。
    /// 聚焦时容器边框切到 Theme.Accent。
    /// </summary>
    internal sealed class ThemedTextBox : Panel
    {
        private const int CornerRadius = 6;
        private const int InnerPadX = 8;

        private static readonly Color FillColor = Color.FromArgb(255, 28, 28, 30);
        private static readonly Color BorderIdle = Color.FromArgb(60, 255, 255, 255);

        private readonly TextBox _inner;

        public ThemedTextBox()
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.FromArgb(40, 40, 42); // 与窗口背景同色，避免容器外圈出现亮边
            this.Padding = new Padding(InnerPadX, 0, InnerPadX, 0);

            _inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = FillColor,
                ForeColor = Theme.IconColorActive,
                Font = Theme.UiFont(9.5f),
            };
            _inner.GotFocus += (s, e) => Invalidate();
            _inner.LostFocus += (s, e) => Invalidate();
            this.Controls.Add(_inner);
        }

        public TextBox Inner => _inner;

        public bool ReadOnly
        {
            get { return _inner.ReadOnly; }
            set { _inner.ReadOnly = value; }
        }

        public override string Text
        {
            get { return _inner.Text; }
            set { _inner.Text = value; }
        }

        public bool TabStopInner
        {
            get { return _inner.TabStop; }
            set { _inner.TabStop = value; }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutInner();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            _inner.Font = this.Font;
            LayoutInner();
        }

        private void LayoutInner()
        {
            // TextBox 高度由其字体决定，垂直居中放在容器内
            int innerHeight = _inner.PreferredHeight;
            _inner.SetBounds(
                this.Padding.Left,
                Math.Max(0, (this.Height - innerHeight) / 2),
                Math.Max(0, this.Width - this.Padding.Left - this.Padding.Right),
                innerHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = GraphicsHelpers.RoundRect(rect, CornerRadius))
            {
                using (var fill = new SolidBrush(FillColor))
                    g.FillPath(fill, path);

                Color borderColor = _inner.Focused ? Theme.Accent : BorderIdle;
                using (var pen = new Pen(borderColor, _inner.Focused ? 1.4f : 1f))
                    g.DrawPath(pen, path);
            }
        }
    }
}
