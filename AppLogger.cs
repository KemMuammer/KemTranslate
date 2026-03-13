using System;
using System.IO;

namespace KemTranslate
{
    internal enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        None
    }

    internal static class AppLogger
    {
        private static readonly object SyncRoot = new();
        private static LogLevel _minimumLevel = LogLevel.Info;

        public static string LogDirectoryPath => Path.Combine(AppContext.BaseDirectory, "logs");

        private static string GetLogPath()
        {
            return Path.Combine(LogDirectoryPath, $"kemtranslate-{DateTime.Now:yyyyMMdd}.log");
        }

        public static void Configure(string? levelText)
        {
            if (!Enum.TryParse(levelText, true, out LogLevel parsedLevel))
                parsedLevel = LogLevel.Info;

            _minimumLevel = parsedLevel;

            try
            {
                Directory.CreateDirectory(LogDirectoryPath);
            }
            catch
            {
            }
        }

        public static void Log(string message)
        {
            Log(LogLevel.Info, message);
        }

        public static void Log(LogLevel level, string message)
        {
            if (!ShouldLog(level))
                return;

            try
            {
                Directory.CreateDirectory(LogDirectoryPath);
                lock (SyncRoot)
                {
                    File.AppendAllText(GetLogPath(), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }

        public static void Log(Exception ex, string context)
        {
            Log(LogLevel.Error, $"{context}: {ex}");
        }

        private static bool ShouldLog(LogLevel level)
        {
            if (_minimumLevel == LogLevel.None)
                return false;

            return level >= _minimumLevel;
        }
    }
}
