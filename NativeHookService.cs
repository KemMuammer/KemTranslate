using System;
using System.Runtime.InteropServices;

namespace KemTranslate
{
    internal sealed class NativeHookService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_LBUTTONUP = 0x0202;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private readonly LowLevelKeyboardProc _keyboardProc;
        private readonly LowLevelKeyboardProc _mouseProc;
        private IntPtr _keyboardHookId;
        private IntPtr _mouseHookId;

        public NativeHookService()
        {
            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;
        }

        public event Action<int, bool, bool, bool, bool>? KeyPressed;
        public event Action? MouseLeftButtonReleased;

        public void Install()
        {
            _keyboardHookId = SetHook(_keyboardProc, WH_KEYBOARD_LL);
            _mouseHookId = SetHook(_mouseProc, WH_MOUSE_LL);
        }

        public void Uninstall()
        {
            try
            {
                if (_keyboardHookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_keyboardHookId);
                    _keyboardHookId = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "Failed to unhook keyboard hook");
            }

            try
            {
                if (_mouseHookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_mouseHookId);
                    _mouseHookId = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "Failed to unhook mouse hook");
            }
        }

        public void Dispose()
        {
            Uninstall();
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                try
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    KeyPressed?.Invoke(
                        vkCode,
                        (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
                        (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
                        (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
                        (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0);
                }
                catch (Exception ex)
                {
                    AppLogger.Log(ex, "Keyboard hook callback failed");
                }
            }

            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONUP)
                MouseLeftButtonReleased?.Invoke();

            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc, int hookType)
        {
            using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var currentModule = currentProcess.MainModule;
            IntPtr moduleHandle = GetModuleHandle(currentModule?.ModuleName ?? string.Empty);
            return SetWindowsHookEx(hookType, proc, moduleHandle, 0);
        }
    }
}
