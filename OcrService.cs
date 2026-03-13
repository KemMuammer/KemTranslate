using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KemTranslate
{
    internal sealed class OcrService
    {
        public static string BundledOcrDirectoryPath => Path.Combine(AppContext.BaseDirectory, "ocr");
        public static string BundledExecutablePath => Path.Combine(BundledOcrDirectoryPath, "tesseract.exe");

        private readonly Func<AppSettings> _settingsProvider;

        public OcrService(Func<AppSettings> settingsProvider)
        {
            _settingsProvider = settingsProvider;
        }

        public async Task<string> RecognizeTextAsync(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            string? tesseractPath = FindTesseractPath();
            if (string.IsNullOrWhiteSpace(tesseractPath))
                throw new InvalidOperationException("OCR requires Tesseract. Install tesseract.exe or place it next to the application.");

            var ocrLanguage = (_settingsProvider().OcrLanguage ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ocrLanguage))
                ocrLanguage = "eng";

            string imagePath = Path.Combine(Path.GetTempPath(), $"kemtranslate-ocr-{Guid.NewGuid():N}.png");
            string outputBasePath = Path.Combine(Path.GetTempPath(), $"kemtranslate-ocr-{Guid.NewGuid():N}");
            string outputTextPath = outputBasePath + ".txt";

            try
            {
                bitmap.Save(imagePath, ImageFormat.Png);

                var startInfo = new ProcessStartInfo
                {
                    FileName = tesseractPath,
                    Arguments = $"\"{imagePath}\" \"{outputBasePath}\" -l \"{ocrLanguage}\" --psm 6",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.GetDirectoryName(tesseractPath) ?? AppContext.BaseDirectory
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    throw new InvalidOperationException("Failed to start Tesseract OCR.");

                string stdError = await process.StandardError.ReadToEndAsync();
                string stdOutput = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    string message = string.IsNullOrWhiteSpace(stdError) ? stdOutput : stdError;
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Tesseract OCR failed." : message.Trim());
                }

                if (!File.Exists(outputTextPath))
                    return string.Empty;

                return (await File.ReadAllTextAsync(outputTextPath)).Trim();
            }
            finally
            {
                TryDeleteFile(imagePath);
                TryDeleteFile(outputTextPath);
            }
        }

        public bool IsOcrAvailable()
        {
            try
            {
                return !string.IsNullOrWhiteSpace(FindTesseractPath());
            }
            catch
            {
                return false;
            }
        }

        public bool HasBundledOcrFiles()
        {
            return File.Exists(BundledExecutablePath);
        }

        private string? FindTesseractPath()
        {
            var configuredPath = (_settingsProvider().OcrExecutablePath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (File.Exists(configuredPath))
                    return configuredPath;

                throw new InvalidOperationException($"Configured OCR executable was not found: {configuredPath}");
            }

            string[] candidates =
            [
                BundledExecutablePath,
                Path.Combine(AppContext.BaseDirectory, "tesseract.exe"),
                @"C:\Program Files\Tesseract-OCR\tesseract.exe",
                @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe"
            ];

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
            {
                try
                {
                    var candidate = Path.Combine(directory, "tesseract.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                }
            }

            return null;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
