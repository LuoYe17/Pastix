using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pastix.UI
{
    /// <summary>
    /// 屏幕右下角的轻量级成功提示 Toast。
    /// 独立 TopMost 无激活窗口，生命周期与触发方解耦：
    /// 即使触发它的 Form 立刻关闭，Toast 也会照常停留 1.5s 后淡出。
    /// 单例：连续触发时新的替换旧的，不堆叠。
    /// </summary>
    internal sealed class Toast : Form
    {
        private const int MinWidth = 220;
        private const int MaxWidth = 360;
        private const int FixedHeight = 44;
        private const int CornerRadius = 12;
        private const int IconDiameter = 22;
        private const int LeftPad = 11;
        private const int IconTextGap = 10;
        private const int RightPad = 14;
        private const int ScreenMargin = 24;
        private const int VisibleMs = 1500;
        private const int FadeStepMs = 16;
        private const double FadeStep = 0.08;

        // 不抢焦点 + 不进 Alt-Tab
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int SW_SHOWNOACTIVATE = 4;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static Toast _current;

        private readonly string _text;
        private readonly Timer _timer;
        private bool _fading;

        /// <summary>显示一个成功提示。可在任意线程调用前的 UI 上下文中触发。</summary>
        public static void Show(string text)
        {
            // 替换旧实例：先清 _current 再 Close，避免 OnFormClosed 把新值也清掉
            if (_current != null && !_current.IsDisposed)
            {
                var old = _current;
                _current = null;
                try { old.Close(); } catch { /* 关闭失败也不影响新 toast */ }
            }

            var toast = new Toast(text ?? string.Empty);
            _current = toast;
            toast.ShowAtPrimaryBottomRight();
        }

        private Toast(string text)
        {
            _text = text;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            // 必须使用实色（alpha=255），否则 Form.BackColor 会拒绝；与 AnnotationToolbar 保持一致
            var bg = Theme.ToolbarBackground;
            BackColor = Color.FromArgb(bg.R, bg.G, bg.B);

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer, true);

            // 测量文字宽度，决定窗口宽度（最小 220、最大 360）
            int textWidth;
            using (var font = Theme.UiFont(10f))
            {
                textWidth = TextRenderer.MeasureText(_text, font).Width;
            }
            int contentW = LeftPad + IconDiameter + IconTextGap + textWidth + RightPad;
            Size = new Size(Math.Max(MinWidth, Math.Min(MaxWidth, contentW)), FixedHeight);

            UpdateRegion();

            _timer = new Timer { Interval = VisibleMs };
            _timer.Tick += OnTimerTick;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        private void UpdateRegion()
        {
            using (var path = GraphicsHelpers.RoundRect(new RectangleF(0, 0, Width, Height), CornerRadius))
                this.Region = new Region(path);
        }

        private void ShowAtPrimaryBottomRight()
        {
            // 主显示器 WorkingArea 已自动剔除任务栏区域
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - ScreenMargin, wa.Bottom - Height - ScreenMargin);

            // 通过显式 ShowWindow(SW_SHOWNOACTIVATE) 显示，避免抢焦点
            ShowWindow(this.Handle, SW_SHOWNOACTIVATE);
            Visible = true;
            _timer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            // 第一次 Tick：进入淡出阶段，缩短 Interval
            if (!_fading)
            {
                _fading = true;
                _timer.Interval = FadeStepMs;
                return;
            }

            double op = Opacity - FadeStep;
            if (op <= 0)
            {
                _timer.Stop();
                Close();
                return;
            }
            Opacity = op;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 背景：BackColor + Region 圆角已处理

            // 左侧绿色实心圆
            int iconY = (Height - IconDiameter) / 2;
            var iconRect = new Rectangle(LeftPad, iconY, IconDiameter, IconDiameter);
            using (var brush = new SolidBrush(Theme.Success))
                g.FillEllipse(brush, iconRect);

            // 圆内白色对勾（用统一的 Icons 矢量系统，缩放到 16×16）
            const float CheckSize = 16f;
            var checkRect = new RectangleF(
                LeftPad + (IconDiameter - CheckSize) / 2f,
                iconY + (IconDiameter - CheckSize) / 2f,
                CheckSize, CheckSize);
            Icons.Draw(g, Icons.IconKind.Check, checkRect, Color.White, 2.4f);

            // 文字（垂直居中，超长截断）
            int textX = LeftPad + IconDiameter + IconTextGap;
            var textRect = new Rectangle(textX, 0, Width - textX - RightPad, Height);
            using (var font = Theme.UiFont(10f))
            {
                TextRenderer.DrawText(g, _text, font, textRect, Theme.IconColorActive,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _timer?.Stop();
            _timer?.Dispose();
            if (ReferenceEquals(_current, this)) _current = null;
        }
    }
}
