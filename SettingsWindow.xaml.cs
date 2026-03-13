using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace KemTranslate
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;
        private bool _isApiKeyVisible;
        private bool _syncingApiKeyFields;
        private int _capturedHotkeyVk;
        private int _capturedHotkeyPressCount;
        private int _capturedOcrShortcutVk;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            var installedFonts = Fonts.SystemFontFamilies
                .Select(x => x.Source)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            CmbEditorFontFamily.ItemsSource = installedFonts;

            CmbLogLevel.ItemsSource = Enum.GetNames(typeof(LogLevel));

            _capturedHotkeyVk = _settings.HotkeyVk;
            _capturedHotkeyPressCount = Math.Clamp(_settings.HotkeyPressCount, 1, 2);
            _capturedOcrShortcutVk = _settings.OcrShortcutVk;
            TglCtrl.IsChecked = _settings.CtrlRequired;
            TglAlt.IsChecked = _settings.AltRequired;
            TglShift.IsChecked = _settings.ShiftRequired;
            TglWin.IsChecked = _settings.WinRequired;
            TglOcrCtrl.IsChecked = _settings.OcrShortcutCtrlRequired;
            TglOcrAlt.IsChecked = _settings.OcrShortcutAltRequired;
            TglOcrShift.IsChecked = _settings.OcrShortcutShiftRequired;
            TglOcrWin.IsChecked = _settings.OcrShortcutWinRequired;
            TglCaptureKey.Content = GetHotkeyCaptureDisplay();
            TglOcrCaptureKey.Content = GetOcrShortcutCaptureDisplay();
            TxtThreshold.Text = _settings.ThresholdMs.ToString();
            ChkDark.IsChecked = _settings.IsDarkMode;
            ChkMinimizeToTray.IsChecked = _settings.MinimizeToTrayOnClose;
            ChkStartMinimized.IsChecked = _settings.StartMinimizedToTray;
            ChkStartWithWindows.IsChecked = WindowsStartupHelper.IsEnabled();
            ChkHotkeyOpensMain.IsChecked = _settings.HotkeyOpensMainWindow;
            var preferredFontFamily = string.IsNullOrWhiteSpace(_settings.EditorFontFamily) ? "Arial" : _settings.EditorFontFamily;
            CmbEditorFontFamily.SelectedItem = installedFonts.FirstOrDefault(x => x.Equals(preferredFontFamily, StringComparison.CurrentCultureIgnoreCase))
                ?? installedFonts.FirstOrDefault(x => x.Equals("Arial", StringComparison.CurrentCultureIgnoreCase))
                ?? installedFonts.FirstOrDefault();
            TxtEditorFontSize.Text = _settings.EditorFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            CmbLogLevel.SelectedItem = string.IsNullOrWhiteSpace(_settings.LogLevel) ? nameof(LogLevel.Info) : _settings.LogLevel;
            TxtOcrExecutablePath.Text = GetInitialOcrExecutablePath();
            TxtOcrLanguage.Text = string.IsNullOrWhiteSpace(_settings.OcrLanguage) ? "eng" : _settings.OcrLanguage;
            TxtServerUrl.Text = _settings.ServerUrl;
            TxtApiKey.Text = _settings.ApiKey;
            TxtApiKeyPassword.Password = _settings.ApiKey;
            TxtLanguageToolServerUrl.Text = _settings.LanguageToolServerUrl;
            ChkDark.Checked += ChkDark_Checked;
            ChkDark.Unchecked += ChkDark_Unchecked;
            TglCtrl.Checked += HotkeyModifierChanged;
            TglCtrl.Unchecked += HotkeyModifierChanged;
            TglAlt.Checked += HotkeyModifierChanged;
            TglAlt.Unchecked += HotkeyModifierChanged;
            TglShift.Checked += HotkeyModifierChanged;
            TglShift.Unchecked += HotkeyModifierChanged;
            TglWin.Checked += HotkeyModifierChanged;
            TglWin.Unchecked += HotkeyModifierChanged;
            TglOcrCtrl.Checked += OcrShortcutModifierChanged;
            TglOcrCtrl.Unchecked += OcrShortcutModifierChanged;
            TglOcrAlt.Checked += OcrShortcutModifierChanged;
            TglOcrAlt.Unchecked += OcrShortcutModifierChanged;
            TglOcrShift.Checked += OcrShortcutModifierChanged;
            TglOcrShift.Unchecked += OcrShortcutModifierChanged;
            TglOcrWin.Checked += OcrShortcutModifierChanged;
            TglOcrWin.Unchecked += OcrShortcutModifierChanged;
            PreviewKeyDown += SettingsWindow_PreviewKeyDown;
            UpdateHotkeyPreview();
            UpdateOcrShortcutPreview();
            UpdateConnectionFieldPlaceholders();
            NativeWindowThemeHelper.Apply(this, _settings.IsDarkMode);
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            _settings.CtrlRequired = TglCtrl.IsChecked == true;
            _settings.AltRequired = TglAlt.IsChecked == true;
            _settings.ShiftRequired = TglShift.IsChecked == true;
            _settings.WinRequired = TglWin.IsChecked == true;
            _settings.HotkeyVk = _capturedHotkeyVk;
            _settings.HotkeyPressCount = _capturedHotkeyPressCount;
            _settings.OcrShortcutCtrlRequired = TglOcrCtrl.IsChecked == true;
            _settings.OcrShortcutAltRequired = TglOcrAlt.IsChecked == true;
            _settings.OcrShortcutShiftRequired = TglOcrShift.IsChecked == true;
            _settings.OcrShortcutWinRequired = TglOcrWin.IsChecked == true;
            _settings.OcrShortcutVk = _capturedOcrShortcutVk;

            if (int.TryParse(TxtThreshold.Text, out var th) && th >= 50)
                _settings.ThresholdMs = th;

            _settings.IsDarkMode = ChkDark.IsChecked == true;
            _settings.MinimizeToTrayOnClose = ChkMinimizeToTray.IsChecked == true;
            _settings.StartMinimizedToTray = ChkStartMinimized.IsChecked == true;
            _settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
            _settings.HotkeyOpensMainWindow = ChkHotkeyOpensMain.IsChecked == true;
            _settings.HotkeyPreset = TxtHotkeyPreview.Text;
            _settings.LogLevel = (CmbLogLevel.SelectedItem as string) ?? nameof(LogLevel.Info);
            _settings.OcrExecutablePath = (TxtOcrExecutablePath.Text ?? string.Empty).Trim();
            _settings.OcrLanguage = string.IsNullOrWhiteSpace(TxtOcrLanguage.Text) ? "eng" : TxtOcrLanguage.Text.Trim();
            _settings.EditorFontFamily = (CmbEditorFontFamily.SelectedItem as string) ?? "Arial";
            if (double.TryParse(TxtEditorFontSize.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fontSize) && fontSize >= 8)
                _settings.EditorFontSize = fontSize;
            _settings.ServerUrl = (TxtServerUrl.Text ?? string.Empty).Trim();
            _settings.ApiKey = (_isApiKeyVisible ? TxtApiKey.Text : TxtApiKeyPassword.Password ?? string.Empty).Trim();
            _settings.LanguageToolServerUrl = (TxtLanguageToolServerUrl.Text ?? string.Empty).Trim();

            _settings.Save();
            DialogResult = true;
            Close();
        }

        private void BtnToggleApiKeyVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (_isApiKeyVisible)
            {
                TxtApiKeyPassword.Password = TxtApiKey.Text ?? string.Empty;
                TxtApiKey.Visibility = Visibility.Collapsed;
                TxtApiKeyPassword.Visibility = Visibility.Visible;
                BtnToggleApiKeyVisibility.Content = "👁";
                _isApiKeyVisible = false;
                UpdateConnectionFieldPlaceholders();
                TxtApiKeyPassword.Focus();
                return;
            }

            TxtApiKey.Text = TxtApiKeyPassword.Password;
            TxtApiKeyPassword.Visibility = Visibility.Collapsed;
            TxtApiKey.Visibility = Visibility.Visible;
            BtnToggleApiKeyVisibility.Content = "🙈";
            _isApiKeyVisible = true;
            UpdateConnectionFieldPlaceholders();
            TxtApiKey.Focus();
            TxtApiKey.CaretIndex = TxtApiKey.Text.Length;
        }

        private void TxtApiKeyPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingApiKeyFields)
                return;

            _syncingApiKeyFields = true;
            TxtApiKey.Text = TxtApiKeyPassword.Password;
            _syncingApiKeyFields = false;
            UpdateConnectionFieldPlaceholders();
        }

        private void TxtApiKey_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_syncingApiKeyFields)
                return;

            _syncingApiKeyFields = true;
            TxtApiKeyPassword.Password = TxtApiKey.Text ?? string.Empty;
            _syncingApiKeyFields = false;
            UpdateConnectionFieldPlaceholders();
        }

        private void ConnectionField_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateConnectionFieldPlaceholders();
        }

        private void UpdateConnectionFieldPlaceholders()
        {
            TxtServerUrlPlaceholder.Visibility = string.IsNullOrWhiteSpace(TxtServerUrl.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            TxtLanguageToolServerUrlPlaceholder.Visibility = string.IsNullOrWhiteSpace(TxtLanguageToolServerUrl.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            var isApiKeyEmpty = _isApiKeyVisible
                ? string.IsNullOrWhiteSpace(TxtApiKey.Text)
                : string.IsNullOrWhiteSpace(TxtApiKeyPassword.Password);

            TxtApiKeyPlaceholder.Visibility = isApiKeyEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            PreviewTheme(_settings.IsDarkMode);
            DialogResult = false;
            Close();
        }

        private void ChkDark_Checked(object sender, RoutedEventArgs e)
        {
            PreviewTheme(true);
        }

        private void ChkDark_Unchecked(object sender, RoutedEventArgs e)
        {
            PreviewTheme(false);
        }

        private void PreviewTheme(bool dark)
        {
            try
            {
                NativeWindowThemeHelper.ApplyApplicationTheme(dark);

                if (Owner is MainWindow mw)
                {
                    NativeWindowThemeHelper.ApplyWindowBrushes(mw);
                    NativeWindowThemeHelper.Apply(mw, dark);
                }

                NativeWindowThemeHelper.ApplyWindowBrushes(this);
                NativeWindowThemeHelper.Apply(this, dark);
            }
            catch { }
        }

        private void HotkeyModifierChanged(object sender, RoutedEventArgs e)
        {
            UpdateHotkeyPreview();
        }

        private void OcrShortcutModifierChanged(object sender, RoutedEventArgs e)
        {
            UpdateOcrShortcutPreview();
        }

        private void TglCaptureKey_Checked(object sender, RoutedEventArgs e)
        {
            TglCaptureKey.Content = "Taste...";
            TglCaptureKey.Focus();
        }

        private void TglCaptureKey_Unchecked(object sender, RoutedEventArgs e)
        {
            TglCaptureKey.Content = GetHotkeyCaptureDisplay();
        }

        private void BtnResetHotkey_Click(object sender, RoutedEventArgs e)
        {
            TglCtrl.IsChecked = true;
            TglAlt.IsChecked = false;
            TglShift.IsChecked = false;
            TglWin.IsChecked = false;
            _capturedHotkeyVk = 0x43;
            _capturedHotkeyPressCount = 1;
            TglCaptureKey.IsChecked = false;
            TglCaptureKey.Content = GetHotkeyCaptureDisplay();
            UpdateHotkeyPreview();
        }

        private void TglOcrCaptureKey_Checked(object sender, RoutedEventArgs e)
        {
            TglOcrCaptureKey.Content = "Taste...";
            TglOcrCaptureKey.Focus();
        }

        private void TglOcrCaptureKey_Unchecked(object sender, RoutedEventArgs e)
        {
            TglOcrCaptureKey.Content = GetOcrShortcutCaptureDisplay();
        }

        private void BtnResetOcrShortcut_Click(object sender, RoutedEventArgs e)
        {
            TglOcrCtrl.IsChecked = true;
            TglOcrAlt.IsChecked = false;
            TglOcrShift.IsChecked = true;
            TglOcrWin.IsChecked = false;
            _capturedOcrShortcutVk = 0x4F;
            TglOcrCaptureKey.IsChecked = false;
            TglOcrCaptureKey.Content = GetOcrShortcutCaptureDisplay();
            UpdateOcrShortcutPreview();
        }

        private void BtnOpenOcrFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(OcrService.BundledOcrDirectoryPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{OcrService.BundledOcrDirectoryPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "BtnOpenOcrFolder_Click failed");
                global::System.Windows.MessageBox.Show(this, "Failed to open OCR folder: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetInitialOcrExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(_settings.OcrExecutablePath))
                return _settings.OcrExecutablePath;

            return File.Exists(OcrService.BundledExecutablePath)
                ? OcrService.BundledExecutablePath
                : string.Empty;
        }

        private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return;

            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey == 0)
                return;

            if (TglOcrCaptureKey.IsChecked == true)
            {
                _capturedOcrShortcutVk = virtualKey;
                TglOcrCaptureKey.IsChecked = false;
                UpdateOcrShortcutPreview();
                e.Handled = true;
                return;
            }

            if (TglCaptureKey.IsChecked != true)
                return;

            if (virtualKey == _capturedHotkeyVk && _capturedHotkeyPressCount == 1)
            {
                _capturedHotkeyPressCount = 2;
                TglCaptureKey.IsChecked = false;
            }
            else
            {
                _capturedHotkeyVk = virtualKey;
                _capturedHotkeyPressCount = 1;
            }

            UpdateHotkeyPreview();
            e.Handled = true;
        }

        private void UpdateHotkeyPreview()
        {
            TxtHotkeyPreview.Text = BuildHotkeyDisplay();
            if (TglCaptureKey.IsChecked != true)
                TglCaptureKey.Content = GetHotkeyCaptureDisplay();
        }

        private void UpdateOcrShortcutPreview()
        {
            TxtOcrShortcutPreview.Text = BuildOcrShortcutDisplay();
            if (TglOcrCaptureKey.IsChecked != true)
                TglOcrCaptureKey.Content = GetOcrShortcutCaptureDisplay();
        }

        private string BuildHotkeyDisplay()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (TglCtrl.IsChecked == true) parts.Add("Ctrl");
            if (TglAlt.IsChecked == true) parts.Add("Alt");
            if (TglShift.IsChecked == true) parts.Add("Shift");
            if (TglWin.IsChecked == true) parts.Add("Win");
            var keyDisplay = GetHotkeyKeyDisplay(_capturedHotkeyVk);
            parts.Add(keyDisplay);
            if (_capturedHotkeyPressCount > 1)
                parts.Add(keyDisplay);
            return string.Join("+", parts);
        }

        private string GetHotkeyCaptureDisplay()
        {
            var keyDisplay = GetHotkeyKeyDisplay(_capturedHotkeyVk);
            return _capturedHotkeyPressCount > 1 ? keyDisplay + keyDisplay : keyDisplay;
        }

        private string BuildOcrShortcutDisplay()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (TglOcrCtrl.IsChecked == true) parts.Add("Ctrl");
            if (TglOcrAlt.IsChecked == true) parts.Add("Alt");
            if (TglOcrShift.IsChecked == true) parts.Add("Shift");
            if (TglOcrWin.IsChecked == true) parts.Add("Win");
            parts.Add(GetHotkeyKeyDisplay(_capturedOcrShortcutVk));
            return string.Join("+", parts);
        }

        private string GetOcrShortcutCaptureDisplay()
        {
            return GetHotkeyKeyDisplay(_capturedOcrShortcutVk);
        }

        private static string GetHotkeyKeyDisplay(int virtualKey)
        {
            if (virtualKey >= 0x30 && virtualKey <= 0x5A)
                return ((char)virtualKey).ToString();

            var key = KeyInterop.KeyFromVirtualKey(virtualKey);
            return key.ToString();
        }
    }
}
