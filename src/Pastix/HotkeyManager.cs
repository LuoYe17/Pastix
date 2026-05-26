using System;
using System.Text;
using System.Windows.Forms;
using Pastix.Native;

namespace Pastix
{
    /// <summary>
    /// 管理全局热键注册/注销。
    /// </summary>
    internal sealed class HotkeyManager : IDisposable
    {
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;

        private const int HOTKEY_ID = 9001;
        private readonly IntPtr _hwnd;
        private bool _registered;

        public HotkeyManager(IntPtr hwnd)
        {
            _hwnd = hwnd;
        }

        public bool Register(Keys key, uint modifiers = 0)
        {
            Unregister();
            _registered = NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID, modifiers, (uint)key);
            return _registered;
        }

        public void Unregister()
        {
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID);
                _registered = false;
            }
        }

        public void Dispose()
        {
            Unregister();
        }

        /// <summary>
        /// 把 Keys + 修饰符位标志格式化为可读字符串，例如 "Ctrl+Alt+A"。
        /// </summary>
        public static string Format(Keys key, uint modifiers)
        {
            var sb = new StringBuilder();
            if ((modifiers & MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((modifiers & MOD_ALT) != 0) sb.Append("Alt+");
            if ((modifiers & MOD_SHIFT) != 0) sb.Append("Shift+");
            sb.Append(key.ToString());
            return sb.ToString();
        }
    }
}
