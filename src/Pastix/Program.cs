using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Pastix.Native;
using Pastix.UI;

namespace Pastix
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            _mutex = new Mutex(true, "Pastix_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Pastix 已在运行中。", "Pastix", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) => ShowFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex) ShowFatal(ex);
            };

            Application.Run(new MainForm());
        }

        private static void ShowFatal(Exception ex)
        {
            try
            {
                MessageBox.Show(
                    ex.ToString(),
                    "Pastix 出错了",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { }
        }
    }

    /// <summary>
    /// 隐藏的主窗口，承载托盘、热键消息循环与剪贴板监听。
    /// </summary>
    internal sealed class MainForm : Form
    {
        // MOD_WIN 在 HotkeyManager 中未提供常量；这里就近声明，避免修改基础设施文件
        private const uint MOD_WIN = 0x0008;
        private const int PasteDelayMs = 80;

        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _showHistoryMenuItem;
        private HotkeyManager _hotkeyManager;
        private ClipboardWatcher _clipboard;
        private HistoryWindow _window;
        private Settings _settings;
        private string _hotkeyLabel = "Ctrl+Shift+V";
        private string _pendingPaste;
        private System.Windows.Forms.Timer _pasteTimer;

        public MainForm()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.None;
            Opacity = 0;
            Size = new Size(0, 0);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Visible = false;

            _settings = Settings.Load();

            InitTray();

            _clipboard = new ClipboardWatcher(Handle) { MaxItems = _settings.MaxHistoryItems };
            _clipboard.Start();
            // 启动时若磁盘恢复出来的条目已超过新上限，立即裁剪
            _clipboard.Trim();

            _hotkeyManager = new HotkeyManager(Handle);
            RegisterConfiguredHotkey();

            _pasteTimer = new System.Windows.Forms.Timer { Interval = PasteDelayMs };
            _pasteTimer.Tick += OnPasteTick;

            // 仅首次启动弹欢迎引导，避免每次重启打扰
            if (!_settings.FirstRunCompleted)
            {
                _settings.FirstRunCompleted = true;
                _settings.Save();
                BeginInvoke(new Action(() =>
                {
                    MessageBox.Show(
                        $"Pastix 已驻留在系统托盘。\n\n按 {_hotkeyLabel} 打开剪贴板历史。\n右键托盘图标可以退出。",
                        "Pastix",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }));
            }

            // 启动计数 +1（用于第二次启动时引导开机自启）
            _settings.LaunchCount++;
            _settings.Save();

            // 第 2 次及之后的启动时引导（且尚未开自启 + 尚未"不再询问"）
            // 用 >=2：用户选"稍后"（No）后下次启动还会满足触发条件
            if (_settings.LaunchCount >= 2 && !_settings.AutoStart && !_settings.AutoStartPromptShown)
            {
                BeginInvoke(new Action(ShowAutoStartPrompt));
            }
        }

        private void ShowAutoStartPrompt()
        {
            var result = MessageBox.Show(
                "Pastix 需要在后台运行才能记录你复制的内容。\n\n" +
                "建议开启 Windows 启动时自动运行。",
                "Pastix",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (result == DialogResult.Yes)
            {
                // 现在开
                _settings.AutoStart = true;
                _settings.AutoStartPromptShown = true;
                _settings.Save();
                SettingsForm.ApplyAutoStart(true);
                Toast.Show("已开启开机自启");
            }
            else if (result == DialogResult.Cancel)
            {
                // 不再询问
                _settings.AutoStartPromptShown = true;
                _settings.Save();
            }
            // No 是"稍后"：什么都不做，下次启动还会满足触发条件
        }

        private void InitTray()
        {
            var menu = new ContextMenuStrip();
            _showHistoryMenuItem = new ToolStripMenuItem("显示历史", null, (s, e) => ShowHistory());
            menu.Items.Add(_showHistoryMenuItem);
            menu.Items.Add("设置…", null, (s, e) => ShowSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => ExitApp());

            _trayIcon = new NotifyIcon
            {
                Text = "Pastix - 剪贴板历史",
                Icon = AppIcon.Create(32, darkBackground: true),
                Visible = true,
                ContextMenuStrip = menu,
            };
            _trayIcon.DoubleClick += (s, e) => ShowHistory();
        }

        /// <summary>
        /// 用 _settings 中的热键注册全局热键。默认值（Ctrl+Shift+V）注册失败时
        /// 降级到 Win+Shift+V，对应原版本的兜底体验。
        /// </summary>
        private void RegisterConfiguredHotkey()
        {
            bool isDefault =
                _settings.HotkeyKey == (int)Keys.V &&
                (uint)_settings.HotkeyModifiers == (HotkeyManager.MOD_CONTROL | HotkeyManager.MOD_SHIFT);

            if (_hotkeyManager.Register((Keys)_settings.HotkeyKey, (uint)_settings.HotkeyModifiers))
            {
                _hotkeyLabel = HotkeyManager.Format((Keys)_settings.HotkeyKey, (uint)_settings.HotkeyModifiers);
            }
            else if (isDefault && _hotkeyManager.Register(Keys.V, MOD_WIN | HotkeyManager.MOD_SHIFT))
            {
                _hotkeyLabel = "Win+Shift+V";
            }
            else
            {
                _hotkeyLabel = "(热键被占用)";
            }

            if (_showHistoryMenuItem != null)
                _showHistoryMenuItem.Text = $"显示历史 ({_hotkeyLabel})";
        }

        private void ShowSettings()
        {
            using (var form = new SettingsForm(_settings, _hotkeyManager, _clipboard))
            {
                form.ShowDialog();
            }

            // 外部契约：无论用户按 OK / Cancel / 直接关，都重新加载一次配置并同步运行时状态。
            // 这样 SettingsForm 内部不必把"成功状态"通知出来，主程序只关心磁盘是真相。
            _settings = Settings.Load();
            RegisterConfiguredHotkey();

            if (_clipboard != null)
            {
                _clipboard.MaxItems = _settings.MaxHistoryItems;
                _clipboard.Trim();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                ShowHistory();
            }
            else if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                _clipboard?.OnClipboardUpdate();
            }
            base.WndProc(ref m);
        }

        private void ShowHistory()
        {
            // 已经打开则切换为关闭，避免重复弹出
            if (_window != null && !_window.IsDisposed && _window.Visible)
            {
                _window.Close();
                _window = null;
                return;
            }

            _window = new HistoryWindow();
            _window.ItemChosen += OnItemChosen;
            _window.Cancelled += OnCancelled;
            _window.ItemPinToggleRequested += OnItemPinToggleRequested;
            _window.ItemRemoveRequested += OnItemRemoveRequested;
            _window.FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_window, s))
                {
                    _clipboard.Changed -= OnClipboardChanged;
                    _window = null;
                }
            };

            // 订阅历史变更：浮窗可见期间内任何 pin/remove 都立即刷新
            _clipboard.Changed += OnClipboardChanged;

            NativeMethods.GetCursorPos(out var pt);
            _window.ShowWith(_clipboard.Snapshot(), new Point(pt.X, pt.Y));
        }

        private void OnItemPinToggleRequested(ClipboardItem item)
        {
            _clipboard?.TogglePin(item);
        }

        private void OnItemRemoveRequested(ClipboardItem item)
        {
            _clipboard?.RemoveItem(item);
        }

        private void OnClipboardChanged()
        {
            // Changed 可能由后台剪贴板变化触发；仅在浮窗可见时刷新视图
            if (_window != null && !_window.IsDisposed && _window.Visible)
            {
                if (_window.InvokeRequired)
                    _window.BeginInvoke(new Action(() => _window.RefreshItems(_clipboard.Snapshot())));
                else
                    _window.RefreshItems(_clipboard.Snapshot());
            }
        }

        private void OnItemChosen(string text)
        {
            if (_window != null)
            {
                _window.Close();
                _window = null;
            }

            if (string.IsNullOrEmpty(text)) return;

            try
            {
                _clipboard.SuppressNext(text);
                Clipboard.SetText(text);
            }
            catch
            {
                // 剪贴板被独占时静默放弃，符合"动作即反馈"原则
                return;
            }

            // 等焦点回到原前台窗口，再注入 Ctrl+V
            _pendingPaste = text;
            _pasteTimer.Stop();
            _pasteTimer.Start();
        }

        private void OnPasteTick(object sender, EventArgs e)
        {
            _pasteTimer.Stop();
            if (_pendingPaste == null) return;
            _pendingPaste = null;

            NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(NativeMethods.VK_V, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(NativeMethods.VK_V, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void OnCancelled()
        {
            if (_window != null)
            {
                _window.Close();
                _window = null;
            }
        }

        private void ExitApp()
        {
            _hotkeyManager?.Dispose();
            _clipboard?.Dispose();
            _pasteTimer?.Dispose();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _hotkeyManager?.Dispose();
            _clipboard?.Dispose();
            _pasteTimer?.Dispose();
            _trayIcon?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
