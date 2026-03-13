using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace KemTranslate
{
    public class AppSettings
    {
        public bool CtrlRequired { get; set; } = true;
        public bool AltRequired { get; set; } = false;
        public bool ShiftRequired { get; set; } = false;
        public bool WinRequired { get; set; } = false;
        public int HotkeyVk { get; set; } = 0x43; // 'C'
        public int HotkeyPressCount { get; set; } = 1;
        public string HotkeyPreset { get; set; } = "Ctrl+C";
        public int ThresholdMs { get; set; } = 600;
        public bool IsDarkMode { get; set; } = true;
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public bool StartMinimizedToTray { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool HotkeyOpensMainWindow { get; set; } = false;
        public bool HasSeenOcrSetupPrompt { get; set; } = false;
        public string LogLevel { get; set; } = "Info";
        public bool OcrShortcutCtrlRequired { get; set; } = true;
        public bool OcrShortcutAltRequired { get; set; } = false;
        public bool OcrShortcutShiftRequired { get; set; } = true;
        public bool OcrShortcutWinRequired { get; set; } = false;
        public int OcrShortcutVk { get; set; } = 0x4F; // 'O'
        public string OcrExecutablePath { get; set; } = "";
        public string OcrLanguage { get; set; } = "eng";
        public string EditorFontFamily { get; set; } = "Arial";
        public double EditorFontSize { get; set; } = 20;
        public string ServerUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string LanguageToolServerUrl { get; set; } = "";
        public List<string> FavoriteTargetLanguages { get; set; } = new();
        public List<HistoryEntry> RecentTranslations { get; set; } = new();
        public List<HistoryEntry> RecentWrites { get; set; } = new();

        private static string GetPath() => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "kemsettings.json");

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this);
                File.WriteAllText(GetPath(), json);
            }
            catch { }
        }

        public static AppSettings Load()
        {
            try
            {
                var p = GetPath();
                if (File.Exists(p))
                {
                    var json = File.ReadAllText(p);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        s.FavoriteTargetLanguages ??= new List<string>();
                        s.RecentTranslations ??= new List<HistoryEntry>();
                        s.RecentWrites ??= new List<HistoryEntry>();

                        bool saveSettings = false;

                        if (!json.Contains("\"IsDarkMode\"", StringComparison.OrdinalIgnoreCase))
                        {
                            s.IsDarkMode = true;
                            saveSettings = true;
                        }

                        if (!json.Contains("\"StartMinimizedToTray\"", StringComparison.OrdinalIgnoreCase))
                        {
                            s.StartMinimizedToTray = true;
                            saveSettings = true;
                        }

                        if (!json.Contains("\"StartWithWindows\"", StringComparison.OrdinalIgnoreCase))
                        {
                            s.StartWithWindows = WindowsStartupHelper.IsEnabled();
                            saveSettings = true;
                        }

                        if (saveSettings)
                            s.Save();

                        return s;
                    }
                }
            }
            catch { }
            return new AppSettings();
        }

        public string GetKeyChar()
        {
            try { return ((char)HotkeyVk).ToString(); } catch { return "C"; }
        }

        public void SetKeyFromChar(char c)
        {
            HotkeyVk = char.ToUpperInvariant(c);
        }

        public string GetDisplayString()
        {
            var parts = new List<string>();
            if (CtrlRequired) parts.Add("Ctrl");
            if (AltRequired) parts.Add("Alt");
            if (ShiftRequired) parts.Add("Shift");
            if (WinRequired) parts.Add("Win");

            var key = GetKeyChar();
            parts.Add(key);
            if (HotkeyPressCount > 1)
                parts.Add(key);

            return string.Join("+", parts);
        }

        public string GetOcrShortcutDisplayString()
        {
            var parts = new List<string>();
            if (OcrShortcutCtrlRequired) parts.Add("Ctrl");
            if (OcrShortcutAltRequired) parts.Add("Alt");
            if (OcrShortcutShiftRequired) parts.Add("Shift");
            if (OcrShortcutWinRequired) parts.Add("Win");

            string key;
            try { key = ((char)OcrShortcutVk).ToString(); } catch { key = "O"; }
            parts.Add(key);
            return string.Join("+", parts);
        }
    }

    public class HistoryEntry
    {
        public string SourceText { get; set; } = "";
        public string ResultText { get; set; } = "";
        public string SourceLanguage { get; set; } = "";
        public string TargetLanguage { get; set; } = "";
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string DisplayText
        {
            get
            {
                var sourcePreview = string.IsNullOrWhiteSpace(SourceText) ? "(empty)" : SourceText.Replace("\r", " ").Replace("\n", " ");
                if (sourcePreview.Length > 36)
                    sourcePreview = sourcePreview[..36] + "…";

                return $"{TimestampUtc.ToLocalTime():HH:mm} {sourcePreview}";
            }
        }
    }
}
