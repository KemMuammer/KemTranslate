using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace KemTranslate
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings = AppSettings.Load();
            AppLogger.Configure(settings.LogLevel);
            AppLogger.Log("Application startup.");
            var ocrService = new OcrService(() => settings);
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            var startMinimizedToTray = settings.StartMinimizedToTray;

            if (startMinimizedToTray)
                mainWindow.PrepareForHiddenStartup();

            if (!settings.HasSeenOcrSetupPrompt && !ocrService.IsOcrAvailable())
                mainWindow.SuppressInitialOcrAvailabilityWarning();

            mainWindow.Show();

            if (!startMinimizedToTray && !settings.HasSeenOcrSetupPrompt && !ocrService.IsOcrAvailable())
            {
                settings.HasSeenOcrSetupPrompt = true;
                settings.Save();

                mainWindow.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    var result = global::System.Windows.MessageBox.Show(
                        mainWindow,
                        $"OCR uses Tesseract. Do you want the app to try installing it automatically now?\n\nIf you choose No, the app will still work normally, but OCR capture will stay unavailable until you install Tesseract or place it in:\n{OcrService.BundledOcrDirectoryPath}",
                        "Set up OCR",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                    {
                        AppLogger.Log(LogLevel.Info, "User skipped Tesseract setup prompt.");
                        return;
                    }

                    var installResult = await TryInstallTesseractAsync();
                    if (installResult.success)
                    {
                        global::System.Windows.MessageBox.Show(
                            mainWindow,
                            "Tesseract installation completed. OCR should now be available. If OCR is still unavailable immediately, restart the app once.",
                            "OCR installed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        AppLogger.Log(LogLevel.Info, "Automatic Tesseract installation completed from first-run prompt.");
                    }
                    else
                    {
                        global::System.Windows.MessageBox.Show(
                            mainWindow,
                            $"Automatic Tesseract installation could not be completed.\n\n{installResult.message}\n\nThe app will continue to work, but OCR capture will remain unavailable until you install Tesseract manually or place it in:\n{OcrService.BundledOcrDirectoryPath}",
                            "OCR installation failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        AppLogger.Log(LogLevel.Warning, $"Automatic Tesseract installation failed: {installResult.message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            if (startMinimizedToTray)
            {
                mainWindow.Dispatcher.BeginInvoke(new Action(mainWindow.HideToTrayAfterStartup), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        private static async Task<(bool success, string message)> TryInstallTesseractAsync()
        {
            string[] packageIds =
            [
                "UB-Mannheim.TesseractOCR",
                "Tesseract-OCR.Tesseract"
            ];

            foreach (var packageId in packageIds)
            {
                var result = await TryInstallTesseractPackageAsync(packageId);
                if (result.success)
                    return result;
            }

            return (false, "No supported automatic Tesseract package installation succeeded. Ensure winget is installed and available.");
        }

        private static async Task<(bool success, string message)> TryInstallTesseractPackageAsync(string packageId)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = $"install --id {packageId} -e --accept-package-agreements --accept-source-agreements --disable-interactivity",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = AppContext.BaseDirectory
                });

                if (process == null)
                    return (false, $"Failed to start winget for package {packageId}.");

                string standardOutput = await process.StandardOutput.ReadToEndAsync();
                string standardError = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                    return (true, $"Installed package {packageId}.");

                var message = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                return (false, string.IsNullOrWhiteSpace(message) ? $"winget returned exit code {process.ExitCode} for package {packageId}." : message.Trim());
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, $"TryInstallTesseractPackageAsync failed for {packageId}");
                return (false, ex.Message);
            }
        }
    }
}
