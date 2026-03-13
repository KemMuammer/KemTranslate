using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace KemTranslate
{
    internal static class WindowsStartupHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "KemTranslate";

        internal static bool IsEnabled()
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                var command = runKey?.GetValue(ValueName) as string;
                if (!string.Equals(GetExecutablePath(command), GetCurrentExecutablePath(), StringComparison.OrdinalIgnoreCase))
                    return false;

                using var startupApprovedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, writable: false);
                var startupApprovedValue = startupApprovedKey?.GetValue(ValueName) as byte[];
                return IsStartupApprovedEnabled(startupApprovedValue);
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "WindowsStartupHelper.IsEnabled failed");
                return false;
            }
        }

        internal static bool TrySetEnabled(bool enabled, out string? errorMessage)
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
                using var startupApprovedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(StartupApprovedRunKeyPath);

                if (runKey == null)
                {
                    errorMessage = "Unable to open the Windows startup registry key.";
                    return false;
                }

                if (startupApprovedKey == null)
                {
                    errorMessage = "Unable to open the Windows startup approval registry key.";
                    return false;
                }

                if (enabled)
                {
                    runKey.SetValue(ValueName, $"\"{GetCurrentExecutablePath()}\"");
                    startupApprovedKey.DeleteValue(ValueName, throwOnMissingValue: false);
                }
                else
                {
                    runKey.DeleteValue(ValueName, throwOnMissingValue: false);
                    startupApprovedKey.DeleteValue(ValueName, throwOnMissingValue: false);
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "WindowsStartupHelper.TrySetEnabled failed");
                errorMessage = ex.Message;
                return false;
            }
        }

        internal static bool IsStartupApprovedEnabled(byte[]? startupApprovedValue)
        {
            if (startupApprovedValue == null || startupApprovedValue.Length == 0)
                return true;

            return startupApprovedValue[0] switch
            {
                0x02 => true,
                0x03 => false,
                0x06 => true,
                0x07 => false,
                _ => true
            };
        }

        private static string GetCurrentExecutablePath()
        {
            return Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

        internal static string? GetExecutablePath(string? command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return null;

            var trimmed = command.Trim();
            if (trimmed.StartsWith('"'))
            {
                var closingQuoteIndex = trimmed.IndexOf('"', 1);
                return closingQuoteIndex > 1 ? trimmed.Substring(1, closingQuoteIndex - 1) : null;
            }

            var firstSpaceIndex = trimmed.IndexOf(' ');
            return firstSpaceIndex > 0 ? trimmed[..firstSpaceIndex] : trimmed;
        }
    }
}
