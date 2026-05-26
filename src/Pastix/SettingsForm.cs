using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Pastix.UI;

namespace Pastix
{
    /// <summary>
    /// 设置面板：自定义热键、历史保留条数、开机自启、清空历史。
    /// 视觉风格与 HistoryWindow 一致：深色磨砂卡片、12px 圆角、Theme.Accent 强调色。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "Pastix";

        private const int CornerRadius = 12;
        private const int TitleBarHeight = 36;

        private readonly Settings _settings;
        private readonly HotkeyManager _hotkeyManager;
        private readonly ClipboardWatcher _clipboard;

        private ThemedTextBox _hotkeyBox;
        private TextButton _hotkeyEditButton;
        private NumericUpDown _maxItemsInput;
        private ThemedCheckBox _autoStartCheck;
        private TextButton _clearButton;

        // 标题栏关闭按钮（命中测试时排除拖动区域）
        private IconButton _closeButton;

        private bool _capturing;
        private Keys _capturedKey;
        private uint _capturedModifiers;
        private Keys _pendingKey;
        private uint _pendingModifiers;

        public SettingsForm(Settings settings, HotkeyManager hotkeyManager, ClipboardWatcher clipboard)
        {
            _settings = settings;
            _hotkeyManager = hotkeyManager;
            _clipboard = clipboard;
            _pendingKey = (Keys)_settings.HotkeyKey;
            _pendingModifiers = (uint)_settings.HotkeyModifiers;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Pastix 设置";
            this.FormBorderStyle = FormBorderStyle.None;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(480, 360);
            this.KeyPreview = true;
            this.BackColor = Color.FromArgb(40, 40, 42);
            this.Font = Theme.UiFont(9.5f);
            this.ForeColor = Theme.IconColorActive;
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);

            UpdateRegion();

            // ---- 布局参数 ----
            int margin = 20;
            int labelWidth = 96;
            int rowHeight = 30;
            int rowGap = 14;
            int contentLeft = margin + labelWidth + 8;
            int contentWidth = this.ClientSize.Width - contentLeft - margin;

            int contentTop = TitleBarHeight + 14;

            // ---- 标题栏关闭按钮 ----
            _closeButton = new IconButton
            {
                Icon = Icons.IconKind.Close,
                Size = new Size(28, 28),
                Location = new Point(this.ClientSize.Width - 28 - 6, (TitleBarHeight - 28) / 2),
                TabStop = false,
            };
            _closeButton.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            this.Controls.Add(_closeButton);

            // ---- 热键行 ----
            int y = contentTop;
            var hotkeyLabel = MakeLabel("截图热键", margin, y, labelWidth, rowHeight);

            _hotkeyBox = new ThemedTextBox
            {
                Location = new Point(contentLeft, y),
                Size = new Size(contentWidth - 80 - 8, rowHeight),
                ReadOnly = true,
                TabStopInner = false,
                Text = HotkeyManager.Format(_pendingKey, _pendingModifiers),
            };
            _hotkeyEditButton = new TextButton
            {
                Style = TextButton.ButtonStyle.Ghost,
                Location = new Point(contentLeft + contentWidth - 80, y),
                Size = new Size(80, rowHeight),
                Text = "修改",
            };
            _hotkeyEditButton.Click += OnHotkeyEditClick;

            // ---- 保留条数行 ----
            y += rowHeight + rowGap;
            var maxLabel = MakeLabel("保留最近", margin, y, labelWidth, rowHeight);

            // NumericUpDown 在深色背景下自带亮底；用一个深色 Panel 把它包起来近似主题化外观。
            var maxWrap = new Panel
            {
                Location = new Point(contentLeft, y),
                Size = new Size(96, rowHeight),
                BackColor = Color.FromArgb(255, 28, 28, 30),
                BorderStyle = BorderStyle.FixedSingle,
            };
            _maxItemsInput = new NumericUpDown
            {
                Minimum = Settings.MinHistoryItems,
                Maximum = Settings.MaxHistoryItemsLimit,
                Increment = 10,
                Value = Math.Max(Settings.MinHistoryItems,
                    Math.Min(Settings.MaxHistoryItemsLimit, _settings.MaxHistoryItems)),
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(255, 28, 28, 30),
                ForeColor = Theme.IconColorActive,
                Font = Theme.UiFont(9.5f),
                TextAlign = HorizontalAlignment.Center,
                Dock = DockStyle.Fill,
            };
            maxWrap.Controls.Add(_maxItemsInput);

            var maxSuffix = new Label
            {
                Text = "条（10 ~ 500）",
                Location = new Point(contentLeft + 96 + 8, y),
                Size = new Size(contentWidth - 96 - 8, rowHeight),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(180, 255, 255, 255),
                BackColor = Color.Transparent,
                Font = Theme.UiFont(9f),
            };

            // ---- 开机自启 ----
            y += rowHeight + rowGap + 4;
            _autoStartCheck = new ThemedCheckBox
            {
                Text = "Windows 启动时自动运行",
                Location = new Point(margin, y),
                Size = new Size(this.ClientSize.Width - margin * 2, 24),
                Checked = _settings.AutoStart,
            };

            // ---- 清空历史按钮（左下） ----
            int btnW = 88;
            int btnH = 32;
            int btnY = this.ClientSize.Height - btnH - margin;

            _clearButton = new TextButton
            {
                Style = TextButton.ButtonStyle.Danger,
                Text = "清空所有历史",
                Size = new Size(120, btnH),
                Location = new Point(margin, btnY),
            };
            _clearButton.Click += OnClearHistoryClick;

            // ---- 底部按钮 ----
            var okButton = new TextButton
            {
                Style = TextButton.ButtonStyle.Accent,
                Text = "确定",
                Size = new Size(btnW, btnH),
                DialogResult = DialogResult.OK,
                Location = new Point(this.ClientSize.Width - btnW * 2 - margin - 8, btnY),
            };
            okButton.Click += OnOkClick;

            var cancelButton = new TextButton
            {
                Style = TextButton.ButtonStyle.Ghost,
                Text = "取消",
                Size = new Size(btnW, btnH),
                DialogResult = DialogResult.Cancel,
                Location = new Point(this.ClientSize.Width - btnW - margin, btnY),
            };

            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;

            this.Controls.AddRange(new Control[]
            {
                hotkeyLabel, _hotkeyBox, _hotkeyEditButton,
                maxLabel, maxWrap, maxSuffix,
                _autoStartCheck,
                _clearButton,
                okButton, cancelButton,
            });
        }

        private static Label MakeLabel(string text, int x, int y, int w, int rowH)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, rowH),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.IconColor,
                BackColor = Color.Transparent,
                Font = Theme.UiFont(9.5f),
            };
        }

        // ---------- 自绘窗口外形 ----------

        private void UpdateRegion()
        {
            using (var path = GraphicsHelpers.RoundRect(new RectangleF(0, 0, Width, Height), CornerRadius))
                this.Region = new Region(path);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 标题栏文字
            var titleRect = new Rectangle(16, 0, this.ClientSize.Width - 16 - 40, TitleBarHeight);
            TextRenderer.DrawText(g, this.Text ?? string.Empty, Theme.UiFont(10f, FontStyle.Regular),
                titleRect, Theme.IconColorActive,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            // 标题栏底部分隔线
            using (var pen = new Pen(Theme.Divider, 1f))
                g.DrawLine(pen, 0, TitleBarHeight, this.ClientSize.Width, TitleBarHeight);

            // 外圈细边框，让圆角更精致
            var border = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = GraphicsHelpers.RoundRect(border, CornerRadius))
            using (var pen = new Pen(Theme.ToolbarBorder, 1f))
                g.DrawPath(pen, path);
        }

        // 标题栏拖动：把标题栏区域报告为标题栏（HTCAPTION），让系统接管拖动
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCAPTION = 2;

            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST)
            {
                int lParam = m.LParam.ToInt32();
                int x = (short)(lParam & 0xFFFF);
                int y = (short)((lParam >> 16) & 0xFFFF);
                var pt = this.PointToClient(new Point(x, y));

                // 关闭按钮区域不算标题栏
                if (_closeButton != null && _closeButton.Bounds.Contains(pt)) return;

                if (pt.Y >= 0 && pt.Y < TitleBarHeight && pt.X >= 0 && pt.X < this.ClientSize.Width)
                {
                    m.Result = (IntPtr)HTCAPTION;
                }
            }
        }

        // ---------- 热键捕获 ----------

        private void OnHotkeyEditClick(object sender, EventArgs e)
        {
            BeginCapture();
        }

        private void BeginCapture()
        {
            _capturing = true;
            _capturedKey = Keys.None;
            _capturedModifiers = 0;
            _hotkeyBox.Text = "请按下新热键（Esc 取消）…";
            _hotkeyEditButton.Enabled = false;
            // 暂停全局热键，避免捕获时触发原热键
            _hotkeyManager?.Unregister();
            // 装 LL 键盘 hook：避免被系统级占用按键（PrintScreen 等）抢占
            LowLevelKeyboardHook.Install(OnHookKey);
            this.Focus();
        }

        private void EndCapture(bool committed)
        {
            // 先卸 hook，避免 EndCapture 流程里再被回调进来
            LowLevelKeyboardHook.Uninstall();

            _capturing = false;
            _hotkeyEditButton.Enabled = true;

            if (committed && _capturedKey != Keys.None)
            {
                _pendingKey = _capturedKey;
                _pendingModifiers = _capturedModifiers;
            }

            _hotkeyBox.Text = HotkeyManager.Format(_pendingKey, _pendingModifiers);
            // 恢复原热键，确保对话框打开期间热键仍可用（按 OK 时会再次替换）
            _hotkeyManager?.Register((Keys)_settings.HotkeyKey, (uint)_settings.HotkeyModifiers);
        }

        /// <summary>
        /// LL 键盘 hook 的回调：在非 UI 线程被调用，须 BeginInvoke 切回 UI。
        /// 返回 true 表示拦下消息（不再下传），用于阻止系统占用按键的默认行为。
        /// </summary>
        private bool OnHookKey(int vkCode)
        {
            if (!_capturing) return false;

            // 修饰键状态来自 hook 内部状态机，不依赖 GetAsyncKeyState：
            // LL hook 一旦 return 1 吞掉 modifier 自身的 KEYDOWN，系统状态机就读不到了
            uint mods = 0;
            if (LowLevelKeyboardHook.IsCtrlDown) mods |= HotkeyManager.MOD_CONTROL;
            if (LowLevelKeyboardHook.IsAltDown) mods |= HotkeyManager.MOD_ALT;
            if (LowLevelKeyboardHook.IsShiftDown) mods |= HotkeyManager.MOD_SHIFT;

            // 拼成 WinForms Keys：低位是 KeyCode，高位是修饰位
            Keys keyData = (Keys)vkCode;
            if ((mods & HotkeyManager.MOD_CONTROL) != 0) keyData |= Keys.Control;
            if ((mods & HotkeyManager.MOD_ALT) != 0) keyData |= Keys.Alt;
            if ((mods & HotkeyManager.MOD_SHIFT) != 0) keyData |= Keys.Shift;

            // 切回 UI 线程处理；BeginInvoke 不阻塞 hook 线程
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((Action)(() =>
                {
                    if (_capturing) HandleCaptureKey(keyData);
                }));
            }

            // 阻断消息继续投递
            return true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // 兜底：无论 OK / Cancel / 直接关闭，都不能留下 hook
            LowLevelKeyboardHook.Uninstall();
            base.OnFormClosed(e);
        }

        private void HandleCaptureKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;

            if (key == Keys.Escape)
            {
                EndCapture(false);
                return;
            }

            // 只是按住修饰键不算完成
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.LMenu || key == Keys.RMenu ||
                key == Keys.None)
            {
                return;
            }

            uint mods = 0;
            if ((keyData & Keys.Control) == Keys.Control) mods |= HotkeyManager.MOD_CONTROL;
            if ((keyData & Keys.Alt) == Keys.Alt) mods |= HotkeyManager.MOD_ALT;
            if ((keyData & Keys.Shift) == Keys.Shift) mods |= HotkeyManager.MOD_SHIFT;

            // 接受：单键（PrintScreen / F1-F12）或 任意修饰 + 任意键
            bool isStandaloneAllowed =
                key == Keys.PrintScreen ||
                (key >= Keys.F1 && key <= Keys.F12);

            if (mods == 0 && !isStandaloneAllowed)
            {
                // 单字母无修饰不算合法，继续等待
                return;
            }

            _capturedKey = key;
            _capturedModifiers = mods;
            EndCapture(true);
        }

        // ---------- 清空历史 ----------

        private void OnClearHistoryClick(object sender, EventArgs e)
        {
            int count = _clipboard?.Snapshot().Count ?? 0;
            var result = MessageBox.Show(
                this,
                $"确定清空所有 {count} 条剪贴板历史？此操作不可撤销。",
                "Pastix",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.OK) return;

            _clipboard?.ClearAll();
        }

        // ---------- 确定 ----------

        private void OnOkClick(object sender, EventArgs e)
        {
            // 1) 校验新热键能否成功注册
            bool hotkeyChanged =
                _pendingKey != (Keys)_settings.HotkeyKey ||
                _pendingModifiers != (uint)_settings.HotkeyModifiers;

            if (hotkeyChanged && _hotkeyManager != null)
            {
                if (!_hotkeyManager.Register(_pendingKey, _pendingModifiers))
                {
                    MessageBox.Show(this, "该热键已被占用，请换一个。", "Pastix",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    // 恢复原热键
                    _hotkeyManager.Register((Keys)_settings.HotkeyKey, (uint)_settings.HotkeyModifiers);
                    this.DialogResult = DialogResult.None;
                    return;
                }
            }

            // 2) 持久化设置
            _settings.HotkeyKey = (int)_pendingKey;
            _settings.HotkeyModifiers = (int)_pendingModifiers;
            _settings.MaxHistoryItems = (int)_maxItemsInput.Value;
            _settings.AutoStart = _autoStartCheck.Checked;
            _settings.Save();

            // 3) 同步注册表 Run 项
            ApplyAutoStart(_settings.AutoStart);

            // 4) 显式关闭：自绘按钮即使实现 IButtonControl，鼠标点击后 Form 也不会自动关
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private static void ApplyAutoStart(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exePath = Assembly.GetEntryAssembly()?.Location;
                        if (string.IsNullOrEmpty(exePath)) return;
                        key.SetValue(RunValueName, "\"" + exePath + "\"");
                    }
                    else
                    {
                        if (key.GetValue(RunValueName) != null)
                            key.DeleteValue(RunValueName, throwOnMissingValue: false);
                    }
                }
            }
            catch
            {
                // 注册表失败静默处理：不阻断设置保存
            }
        }

        // ---------- LL 键盘钩子（仅服务于本对话框的热键捕获） ----------

        /// <summary>
        /// 低级键盘钩子。装上后能拦到被系统占用的按键。
        /// 静态字段必须长期持有 delegate，否则 GC 回收后回调时会崩。
        /// </summary>
        private static class LowLevelKeyboardHook
        {
            private const int WH_KEYBOARD_LL = 13;
            private const int HC_ACTION = 0;
            private const int WM_KEYDOWN = 0x0100;
            private const int WM_SYSKEYDOWN = 0x0104;
            private const int WM_KEYUP = 0x0101;
            private const int WM_SYSKEYUP = 0x0105;

            public const int VK_SHIFT = 0x10;
            public const int VK_CONTROL = 0x11;
            public const int VK_MENU = 0x12;
            public const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
            public const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;
            public const int VK_LMENU = 0xA4, VK_RMENU = 0xA5;

            // 静态字段持有 delegate 防 GC——SetWindowsHookEx 最经典的坑
            private static HookProc _proc;
            private static IntPtr _hook = IntPtr.Zero;
            private static Func<int, bool> _onKey;

            // 内部修饰键状态机：替代 GetAsyncKeyState
            // hook 内部维护，因为 hook 一旦吞掉 modifier 自身的 KEYDOWN，
            // 系统全局键状态就不会更新，GetAsyncKeyState 也读不到了
            private static bool _ctrlDown;
            private static bool _altDown;
            private static bool _shiftDown;

            public static bool IsCtrlDown { get { return _ctrlDown; } }
            public static bool IsAltDown { get { return _altDown; } }
            public static bool IsShiftDown { get { return _shiftDown; } }

            public static void Install(Func<int, bool> onKey)
            {
                if (_hook != IntPtr.Zero) return; // 已装则跳过
                _onKey = onKey;
                _proc = HookCallback;

                // 复位修饰键状态，避免上次安装的残留
                _ctrlDown = false;
                _altDown = false;
                _shiftDown = false;

                // .NET Framework 4.8 上 hMod 必须是有效模块句柄；用主模块名取
                IntPtr hMod;
                using (var proc = Process.GetCurrentProcess())
                {
                    hMod = GetModuleHandle(proc.MainModule.ModuleName);
                }

                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
            }

            public static void Uninstall()
            {
                if (_hook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
                _proc = null;
                _onKey = null;
            }

            private static bool IsCtrlVk(uint vk) { return vk == VK_CONTROL || vk == VK_LCONTROL || vk == VK_RCONTROL; }
            private static bool IsShiftVk(uint vk) { return vk == VK_SHIFT || vk == VK_LSHIFT || vk == VK_RSHIFT; }
            private static bool IsAltVk(uint vk) { return vk == VK_MENU || vk == VK_LMENU || vk == VK_RMENU; }

            private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode == HC_ACTION)
                {
                    int msg = wParam.ToInt32();
                    bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                    if (isDown || isUp)
                    {
                        var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                        uint vk = data.vkCode;

                        // 先维护状态机
                        if (IsCtrlVk(vk)) _ctrlDown = isDown;
                        else if (IsShiftVk(vk)) _shiftDown = isDown;
                        else if (IsAltVk(vk)) _altDown = isDown;
                        else if (isDown)
                        {
                            // 仅"非修饰键的 KEYDOWN"才询问回调是否拦截
                            // modifier 自身永远放行，避免吞掉系统状态机更新
                            var cb = _onKey;
                            if (cb != null && cb((int)vk))
                            {
                                // 返回 1 阻断消息继续传递
                                return (IntPtr)1;
                            }
                        }
                    }
                }
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

            [StructLayout(LayoutKind.Sequential)]
            private struct KBDLLHOOKSTRUCT
            {
                public uint vkCode;
                public uint scanCode;
                public uint flags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern IntPtr GetModuleHandle(string lpModuleName);
        }
    }
}
