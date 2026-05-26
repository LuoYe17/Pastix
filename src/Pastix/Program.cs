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
        private HotkeyManager _hotkeyManager;
        private ClipboardWatcher _clipboard;
        private HistoryWindow _window;
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

            InitTray();

            _clipboard = new ClipboardWatcher(Handle);
            _clipboard.Start();

            _hotkeyManager = new HotkeyManager(Handle);
            RegisterHotkey();

            _pasteTimer = new System.Windows.Forms.Timer { Interval = PasteDelayMs };
            _pasteTimer.Tick += OnPasteTick;

            BeginInvoke(new Action(() =>
            {
                MessageBox.Show(
                    $"Pastix 已驻留在系统托盘。\n\n按 {_hotkeyLabel} 打开剪贴板历史。\n右键托盘图标可以退出。",
                    "Pastix",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }));
        }

        private void InitTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示历史", null, (s, e) => ShowHistory());
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

        private void RegisterHotkey()
        {
            if (_hotkeyManager.Register(Keys.V, HotkeyManager.MOD_CONTROL | HotkeyManager.MOD_SHIFT))
            {
                _hotkeyLabel = "Ctrl+Shift+V";
            }
            else if (_hotkeyManager.Register(Keys.V, MOD_WIN | HotkeyManager.MOD_SHIFT))
            {
                _hotkeyLabel = "Win+Shift+V";
            }
            else
            {
                _hotkeyLabel = "(热键被占用)";
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
            _window.FormClosed += (s, e) => { if (ReferenceEquals(_window, s)) _window = null; };

            NativeMethods.GetCursorPos(out var pt);
            _window.ShowWith(_clipboard.Snapshot(), new Point(pt.X, pt.Y));
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
