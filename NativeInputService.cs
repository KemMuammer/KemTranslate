using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace KemTranslate
{
    internal sealed class NativeInputService
    {
        private const uint WM_COPY = 0x0301;
        private const uint SCI_GETSELECTIONEMPTY = 2650;
        private const int INPUT_KEYBOARD = 1;
        private const ushort KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_CONTROL_KEY = 0x11;
        public const ushort VK_C_KEY = 0x43;
        public const ushort VK_V_KEY = 0x56;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public INPUTUNION U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public IntPtr GetForegroundWindowHandle() => GetForegroundWindow();

        public uint GetProcessIdForWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return 0;

            GetWindowThreadProcessId(handle, out var processId);
            return processId;
        }

        public bool TryGetCursorPos(out POINT point) => GetCursorPos(out point);

        public async Task<string?> CaptureSelectionFromWindowClipboardAsync(IntPtr windowHandle)
        {
            bool sent = await SendCtrlShortcutAsync(windowHandle, VK_C_KEY);
            AppLogger.Log(sent ? "Ctrl+C sent, polling clipboard." : "Ctrl+C send failed, polling clipboard anyway.");

            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(100);
                try
                {
                    if (global::System.Windows.Clipboard.ContainsText())
                    {
                        var candidate = global::System.Windows.Clipboard.GetText();
                        if (!string.IsNullOrWhiteSpace(candidate))
                            return candidate;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log(ex, "Clipboard polling failed");
                }
            }

            return null;
        }

        public async Task<bool> PressCtrlCAsync()
        {
            var foreground = GetForegroundWindow();
            uint foregroundThread = foreground != IntPtr.Zero ? GetWindowThreadProcessId(foreground, out _) : 0;
            uint currentThread = GetCurrentThreadId();
            bool attached = false;

            try
            {
                if (foregroundThread != 0 && AttachThreadInput(currentThread, foregroundThread, true))
                    attached = true;

                if (foreground != IntPtr.Zero)
                {
                    SetForegroundWindow(foreground);
                    var guiThreadInfo = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                    if (GetGUIThreadInfo(foregroundThread, ref guiThreadInfo) && guiThreadInfo.hwndFocus != IntPtr.Zero)
                    {
                        SendMessage(guiThreadInfo.hwndFocus, WM_COPY, IntPtr.Zero, IntPtr.Zero);
                        await Task.Delay(120);
                        return true;
                    }
                }

                SendCtrlShortcut(VK_C_KEY);
                await Task.Delay(180);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "PressCtrlCAsync failed");
                return false;
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        public async Task<bool> SendCtrlShortcutAsync(IntPtr targetWindowHandle, ushort key)
        {
            if (targetWindowHandle == IntPtr.Zero)
                return false;

            uint targetThread = GetWindowThreadProcessId(targetWindowHandle, out _);
            uint currentThread = GetCurrentThreadId();
            bool attached = false;

            try
            {
                if (targetThread != 0 && AttachThreadInput(currentThread, targetThread, true))
                    attached = true;

                SetForegroundWindow(targetWindowHandle);
                SendCtrlShortcut(key);
                await Task.Delay(120);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "SendCtrlShortcutAsync failed");
                return false;
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThread, targetThread, false);
            }
        }

        public bool HasScintillaSelection(IntPtr windowHandle)
        {
            var focusedHandle = GetFocusedWindowHandle(windowHandle);
            if (focusedHandle == IntPtr.Zero)
                return false;

            if (!string.Equals(GetWindowClassName(focusedHandle), "Scintilla", StringComparison.OrdinalIgnoreCase))
                return false;

            return SendMessage(focusedHandle, SCI_GETSELECTIONEMPTY, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero;
        }

        public static string? TryGetSelectedTextViaUIAutomation()
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                if (focused == null)
                    return null;

                if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj) && patternObj is TextPattern textPattern)
                {
                    var selection = textPattern.GetSelection();
                    if (selection != null && selection.Length > 0)
                    {
                        var text = selection[0].GetText(-1)?.Trim();
                        return string.IsNullOrWhiteSpace(text) ? null : text;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "UI Automation selection lookup failed");
            }

            return null;
        }

        private static IntPtr GetFocusedWindowHandle(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return IntPtr.Zero;

            uint threadId = GetWindowThreadProcessId(windowHandle, out _);
            var guiThreadInfo = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            return GetGUIThreadInfo(threadId, ref guiThreadInfo) ? guiThreadInfo.hwndFocus : IntPtr.Zero;
        }

        private static string GetWindowClassName(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return string.Empty;

            var builder = new StringBuilder(256);
            return GetClassName(hWnd, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
        }

        private static void SendCtrlShortcut(ushort key)
        {
            var inputs = new INPUT[]
            {
                new() { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL_KEY } } },
                new() { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = key } } },
                new() { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = key, dwFlags = KEYEVENTF_KEYUP } } },
                new() { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL_KEY, dwFlags = KEYEVENTF_KEYUP } } },
            };

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
