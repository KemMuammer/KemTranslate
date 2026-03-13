using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace KemTranslate
{
    public partial class MainWindow : Window
    {
        // In-app routed commands
        public static readonly RoutedUICommand TranslateCommand = new("Translate", nameof(TranslateCommand), typeof(MainWindow));
        public static readonly RoutedUICommand SwapCommand = new("Swap", nameof(SwapCommand), typeof(MainWindow));
        public static readonly RoutedUICommand ClearCommand = new("Clear", nameof(ClearCommand), typeof(MainWindow));
        public static readonly RoutedUICommand PasteAndTranslateCommand = new("PasteAndTranslate", nameof(PasteAndTranslateCommand), typeof(MainWindow));
        private readonly ObservableCollection<LtLanguage> _languages = new();
        private readonly TranslationService _translationService;
        private readonly WritingService _writingService;
        private readonly NativeInputService _nativeInputService;
        private readonly NativeHookService _nativeHookService;
        private readonly OcrService _ocrService;

        // Global hotkey: double-tap Ctrl+C (press Ctrl+C twice quickly)
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_LBUTTONUP = 0x0202;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12; // Alt
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
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private IntPtr _hookID = IntPtr.Zero;
        private IntPtr _mouseHookID = IntPtr.Zero;
        private DateTime _lastHotkey = DateTime.MinValue;
        private AppSettings _settings = AppSettings.Load();
        private Forms.NotifyIcon? _notifyIcon;
        private bool _exitRequested;
        private FloatingTranslateWindow? _floatingTranslateWindow;
        private IntPtr _lastCapturedWindowHandle = IntPtr.Zero;
        private uint _lastCapturedProcessId;
        private readonly System.Windows.Threading.DispatcherTimer _foregroundMonitorTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly System.Windows.Threading.DispatcherTimer _liveTranslateTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly System.Windows.Threading.DispatcherTimer _liveWriteTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private CancellationTokenSource? _liveTranslateCts;
        private CancellationTokenSource? _liveWriteCts;
        private bool _suppressLiveTranslation;
        private bool _suppressLiveWriting;
        private bool _suppressWriteOutputFormatting;
        private string _lastWriteOutputText = string.Empty;
        private bool _writeOutputReformatPending;
        private string _pendingWriteOutputText = string.Empty;
        private int _pendingWriteOutputCaretOffset;
        private int _hoveredWriteSentenceStart = -1;
        private int _hoveredWriteSentenceLength;
        private int _pinnedWriteSentenceStart = -1;
        private int _pinnedWriteSentenceLength;
        private bool _writeInputHighlightActive;
        private int _writeInputSelectionStart;
        private int _writeInputSelectionLength;
        private bool _ocrAvailabilityWarningShown;
        private bool _deferOcrAvailabilityWarningUntilVisible;
        private static readonly Brush WriteSentenceHighlightBrush = CreateWriteSentenceHighlightBrush();

        // SendInput to simulate Ctrl+C in other apps
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

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
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private const uint WM_COPY = 0x0301;
        private const uint SCI_GETSELECTIONEMPTY = 2650;

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

        private static string GetWindowClassName(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return string.Empty;

            var builder = new StringBuilder(256);
            return GetClassName(hWnd, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
        }

        private static IntPtr GetFocusedWindowHandle(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return IntPtr.Zero;

            uint threadId = GetWindowThreadProcessId(windowHandle, out _);
            var guiThreadInfo = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            return GetGUIThreadInfo(threadId, ref guiThreadInfo) ? guiThreadInfo.hwndFocus : IntPtr.Zero;
        }

        private static bool HasScintillaSelection(IntPtr windowHandle)
        {
            var focusedHandle = GetFocusedWindowHandle(windowHandle);
            if (focusedHandle == IntPtr.Zero)
                return false;

            if (!string.Equals(GetWindowClassName(focusedHandle), "Scintilla", StringComparison.OrdinalIgnoreCase))
                return false;

            return SendMessage(focusedHandle, SCI_GETSELECTIONEMPTY, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero;
        }

        private async Task<string?> CaptureSelectedTextFromWindowAsync(IntPtr windowHandle, bool allowClipboardFallback)
        {
            try
            {
                var selectedViaUIA = NativeInputService.TryGetSelectedTextViaUIAutomation();
                if (!string.IsNullOrWhiteSpace(selectedViaUIA))
                {
                    SetStatus("Captured selection via UI Automation.");
                    return selectedViaUIA;
                }
            }
            catch { }

            if (_nativeInputService.HasScintillaSelection(windowHandle))
            {
                SetStatus("Captured selection via Scintilla copy.");
                return await CaptureSelectionFromWindowClipboardAsync(windowHandle);
            }

            if (!allowClipboardFallback)
                return null;

            SetStatus("Sending Ctrl+C to active window...");
            return await CaptureSelectionFromWindowClipboardAsync(windowHandle);
        }

        private async Task<string?> CaptureSelectionFromWindowClipboardAsync(IntPtr windowHandle)
        {
            return await _nativeInputService.CaptureSelectionFromWindowClipboardAsync(windowHandle);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        private const int INPUT_KEYBOARD = 1;
        private const ushort KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_CONTROL_KEY = 0x11;
        private const ushort VK_C_KEY = 0x43;
        private const ushort VK_V_KEY = 0x56;

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

        public MainWindow()
        {
            NormalizeBundledOcrPathSetting();
            _translationService = new TranslationService(() => _settings);
            _writingService = new WritingService(() => _settings, _translationService);
            _nativeInputService = new NativeInputService();
            _nativeHookService = new NativeHookService();
            _ocrService = new OcrService(() => _settings);

            InitializeComponent();
            ApplyEditorSettings();
            ApplyShortcutSettings();
            RefreshRecentHistoryViews();
            Loaded += async (_, __) =>
            {
                await InitializeAsync();
                WarnAboutMissingOcrIfNeeded();
            };
            Closing += MainWindow_Closing;
            InitializeTrayIcon();
            _foregroundMonitorTimer.Tick += ForegroundMonitorTimer_Tick;
            _liveTranslateTimer.Tick += LiveTranslateTimer_Tick;
            _liveWriteTimer.Tick += LiveWriteTimer_Tick;
            _nativeHookService.KeyPressed += NativeHookService_KeyPressed;
            _nativeHookService.MouseLeftButtonReleased += NativeHookService_MouseLeftButtonReleased;
            // show current configured hotkey in status once initialized
        }

        private void InitializeTrayIcon()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open", null, (_, __) => Dispatcher.Invoke(ShowWindowFromTray));
            menu.Items.Add("Exit", null, (_, __) => Dispatcher.Invoke(ExitFromTray));

            _notifyIcon = new Forms.NotifyIcon
            {
                Text = "KemTranslate",
                ContextMenuStrip = menu,
                Visible = true,
                Icon = GetTrayIcon()
            };

            _notifyIcon.DoubleClick += (_, __) => Dispatcher.Invoke(ShowWindowFromTray);
        }

        private static System.Drawing.Icon GetTrayIcon()
        {
            try
            {
                var iconUri = new Uri("pack://application:,,,/Assets/kemtranslate.ico", UriKind.Absolute);
                var resource = global::System.Windows.Application.GetResourceStream(iconUri);
                if (resource != null)
                {
                    using var stream = resource.Stream;
                    using var icon = new System.Drawing.Icon(stream);
                    return (System.Drawing.Icon)icon.Clone();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "GetTrayIcon failed");
            }

            return System.Drawing.SystemIcons.Application;
        }

        private void ApplyTheme(bool dark)
        {
            try
            {
                NativeWindowThemeHelper.ApplyWindowTheme(this, dark);
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "ApplyTheme failed");
            }
        }

        private void ApplyEditorSettings()
        {
            if (!IsInitialized)
                return;

            var fontFamilyName = string.IsNullOrWhiteSpace(_settings.EditorFontFamily) ? "Arial" : _settings.EditorFontFamily;
            var fontSize = _settings.EditorFontSize >= 8 ? _settings.EditorFontSize : 20;
            var fontFamily = new FontFamily(fontFamilyName);

            TxtInput.FontFamily = fontFamily;
            TxtInput.FontSize = fontSize;
            TxtOutput.FontFamily = fontFamily;
            TxtOutput.FontSize = fontSize;
            TxtWriteInput.FontFamily = fontFamily;
            TxtWriteInput.FontSize = fontSize;
            TxtWriteOutput.FontFamily = fontFamily;
            TxtWriteOutput.FontSize = fontSize;

            _floatingTranslateWindow?.ApplyEditorFontSettings(fontFamily, fontSize);

            if (TxtWriteOutput.Document != null)
                RenderWriteOutput(BuildDiffWriteResult(TxtWriteInput.Text ?? string.Empty, _lastWriteOutputText));
        }

        private void NormalizeBundledOcrPathSetting()
        {
            if (!string.IsNullOrWhiteSpace(_settings.OcrExecutablePath))
                return;

            if (!File.Exists(OcrService.BundledExecutablePath))
                return;

            _settings.OcrExecutablePath = OcrService.BundledExecutablePath;
            _settings.Save();
        }

        private List<LtLanguage> GetOrderedLanguages()
        {
            return _languages
                .OrderByDescending(x => _settings.FavoriteTargetLanguages.Any(f => f.Equals(x.code, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(x => x.name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void RefreshLanguageSources(string? sourceCode = null, string? targetCode = null)
        {
            var orderedLanguages = GetOrderedLanguages();
            var selectedSource = sourceCode ?? (CmbSource.SelectedItem as LtLanguage)?.code ?? "auto";
            var selectedTarget = targetCode ?? (CmbTarget.SelectedItem as LtLanguage)?.code ?? "en";

            var sourceItems = new List<LtLanguage> { new("auto", "Auto-detect") };
            sourceItems.AddRange(orderedLanguages);

            CmbSource.ItemsSource = sourceItems;
            CmbTarget.ItemsSource = orderedLanguages;

            AdjustComboWidth(CmbSource, sourceItems.Select(x => x.display).ToList());
            AdjustComboWidth(CmbTarget, orderedLanguages.Select(x => x.display).ToList());

            int sourceIndex = FindIndexByCode(CmbSource.Items, selectedSource);
            int targetIndex = FindIndexByCode(CmbTarget.Items, selectedTarget);

            CmbSource.SelectedIndex = sourceIndex >= 0 ? sourceIndex : 0;
            CmbTarget.SelectedIndex = targetIndex >= 0 ? targetIndex : (orderedLanguages.Count > 0 ? 0 : -1);
            RefreshFavoriteTargetButton();
        }

        private void RefreshFavoriteTargetButton()
        {
            if (BtnFavoriteTarget == null)
                return;

            var targetCode = (CmbTarget.SelectedItem as LtLanguage)?.code;
            bool isFavorite = !string.IsNullOrWhiteSpace(targetCode)
                && _settings.FavoriteTargetLanguages.Any(x => x.Equals(targetCode, StringComparison.OrdinalIgnoreCase));

            BtnFavoriteTarget.Content = isFavorite ? "★" : "☆";
            BtnFavoriteTarget.ToolTip = isFavorite ? "Remove target language from favorites" : "Add target language to favorites";
        }

        private void AddRecentTranslation(string sourceText, string resultText, string sourceLanguage, string targetLanguage)
        {
            AddRecentHistoryEntry(_settings.RecentTranslations, new HistoryEntry
            {
                SourceText = sourceText,
                ResultText = resultText,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                TimestampUtc = DateTime.UtcNow
            });
            _settings.Save();
            RefreshRecentHistoryViews();
        }

        private void AddRecentWrite(string sourceText, string resultText)
        {
            AddRecentHistoryEntry(_settings.RecentWrites, new HistoryEntry
            {
                SourceText = sourceText,
                ResultText = resultText,
                TimestampUtc = DateTime.UtcNow
            });
            _settings.Save();
            RefreshRecentHistoryViews();
        }

        private static void AddRecentHistoryEntry(List<HistoryEntry> history, HistoryEntry entry)
        {
            history.RemoveAll(x => x.SourceText == entry.SourceText && x.ResultText == entry.ResultText && x.TargetLanguage == entry.TargetLanguage);
            history.Insert(0, entry);
            if (history.Count > 15)
                history.RemoveRange(15, history.Count - 15);
        }

        private void RefreshRecentHistoryViews()
        {
            if (!IsInitialized)
                return;

            CmbRecentTranslations.ItemsSource = null;
            CmbRecentTranslations.ItemsSource = _settings.RecentTranslations;
            CmbRecentTranslations.SelectedItem = null;

            CmbRecentWrites.ItemsSource = null;
            CmbRecentWrites.ItemsSource = _settings.RecentWrites;
            CmbRecentWrites.SelectedItem = null;
        }

        private void ApplyShortcutSettings()
        {
            if (BtnOcrCapture != null)
                BtnOcrCapture.ToolTip = $"Capture a screen region and translate recognized text ({_settings.GetOcrShortcutDisplayString()})";
        }

        private void SaveTextToFile(string text, string title)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("Nothing to save.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = $"KemTranslate-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            File.WriteAllText(dialog.FileName, text);
            SetStatus($"Saved to {Path.GetFileName(dialog.FileName)}.");
        }

        private void WarnAboutMissingOcrIfNeeded()
        {
            if (_ocrAvailabilityWarningShown || _deferOcrAvailabilityWarningUntilVisible)
                return;

            _ocrAvailabilityWarningShown = true;

            if (_ocrService.IsOcrAvailable())
                return;

            const string warningMessage = "OCR is not available. Add Tesseract files to the app's ocr folder or configure a custom tesseract.exe path in Settings.";
            SetStatus(warningMessage);
            AppLogger.Log(LogLevel.Warning, warningMessage);

            if (!IsVisible || !ShowInTaskbar)
                return;

            global::System.Windows.MessageBox.Show(
                this,
                $"OCR is not available.\n\nPlace Tesseract files in:\n{OcrService.BundledOcrDirectoryPath}\n\nor configure a custom tesseract.exe path in Settings.",
                "OCR not configured",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        internal void SuppressInitialOcrAvailabilityWarning()
        {
            _ocrAvailabilityWarningShown = true;
        }

        internal void PrepareForHiddenStartup()
        {
            _deferOcrAvailabilityWarningUntilVisible = true;
            ShowActivated = false;
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }

        internal void HideToTrayAfterStartup()
        {
            HideToTray();
        }


        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            var src = HwndSource.FromHwnd(handle);
            if (src != null)
            {
                src.AddHook(WndProc);
                _nativeHookService.Install();
                _foregroundMonitorTimer.Start();
                SetStatus($"Hook installed: double-tap { _settings.GetDisplayString() }");
                ApplyTheme(_settings.IsDarkMode);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_floatingTranslateWindow != null)
            {
                _floatingTranslateWindow.PrepareForExit();
                _floatingTranslateWindow.Close();
                _floatingTranslateWindow = null;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            _foregroundMonitorTimer.Stop();
            _liveTranslateCts?.Cancel();
            _liveWriteCts?.Cancel();
            _nativeHookService.Uninstall();
            base.OnClosed(e);
        }

        private static uint GetProcessIdForWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return 0;

            GetWindowThreadProcessId(handle, out var processId);
            return processId;
        }

        private void ForegroundMonitorTimer_Tick(object? sender, EventArgs e)
        {
            if (_floatingTranslateWindow?.IsCollapsedMode != true)
                return;

            var foreground = _nativeInputService.GetForegroundWindowHandle();
            if (IsOwnWindowHandle(foreground))
                return;

            var foregroundProcessId = _nativeInputService.GetProcessIdForWindow(foreground);
            if (_lastCapturedProcessId != 0 && foregroundProcessId != 0 && foregroundProcessId != _lastCapturedProcessId)
                _floatingTranslateWindow.HideFloating();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_exitRequested || !_settings.MinimizeToTrayOnClose)
                return;

            e.Cancel = true;
            HideToTray();
        }

        private void HideToTray()
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
            Hide();
        }

        private void ShowWindowFromTray()
        {
            ShowActivated = true;
            ShowInTaskbar = true;
            if (!IsVisible)
                Show();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            ActivateAndBringToFront();

            if (_deferOcrAvailabilityWarningUntilVisible)
            {
                _deferOcrAvailabilityWarningUntilVisible = false;
                WarnAboutMissingOcrIfNeeded();
            }
        }

        private void ExitFromTray()
        {
            _exitRequested = true;
            System.Windows.Application.Current.Shutdown();
        }

        private void ActivateAndBringToFront()
        {
            ShowInTaskbar = true;
            if (!IsVisible)
                Show();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Activate();
            Topmost = true; Topmost = false;
            Focus();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // We use a low level keyboard hook instead of WM_HOTKEY for double-tap detection.
            // Keep existing WndProc behavior for other messages.
            // No action here.
            return IntPtr.Zero;
        }

        private async Task InitializeAsync()
        {
            try
            {
                SetStatus("Loading languages...");
                var langs = await _translationService.GetLanguagesAsync();

                _languages.Clear();
                foreach (var l in langs)
                    _languages.Add(l);
                // Ensure combos are enabled and attach simple diagnostics handlers
                CmbSource.IsEnabled = true; CmbTarget.IsEnabled = true;
                CmbSource.IsHitTestVisible = true; CmbTarget.IsHitTestVisible = true;

                CmbSource.DropDownOpened += (_, __) => SetStatus($"Source items: {CmbSource.Items.Count}");
                CmbTarget.DropDownOpened += (_, __) => SetStatus($"Target items: {CmbTarget.Items.Count}");

                var defaultTargetIndex = _languages
                    .Select((l, i) => new { l, i })
                    .FirstOrDefault(x => x.l.code.Equals("en", StringComparison.OrdinalIgnoreCase))?.i ?? 0;

                var defaultTargetCode = _languages.Count > defaultTargetIndex ? _languages[defaultTargetIndex].code : "en";
                RefreshLanguageSources("auto", defaultTargetCode);
                ApplyEditorSettings();
                RefreshRecentHistoryViews();

                SetStatus("Languages loaded.");
            }
            catch (Exception ex)
            {
                SetStatus("Failed to load languages.");
                global::System.Windows.MessageBox.Show(this, "Error loading languages:\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<List<LtLanguage>> GetLanguagesAsync()
        {
            return await _translationService.GetLanguagesAsync();
        }

        private async Task<string?> DetectSourceLanguageAsync(string text)
        {
            return await _translationService.DetectSourceLanguageAsync(text);
        }

        private async Task<string> TranslateTextAsync(string text, string source, string target)
        {
            return await _translationService.TranslateTextAsync(text, source, target);
        }

        private async Task<LanguageToolWriteResult> ImproveWritingAsync(string text)
        {
            return await _writingService.ImproveWritingAsync(text);
        }

        private static LanguageToolWriteResult BuildLanguageToolWriteResult(string text, IEnumerable<LanguageToolMatch> matches)
        {
            return WritingService.BuildLanguageToolWriteResult(text, matches);
        }

        private static LanguageToolWriteResult BuildDiffWriteResult(string originalText, string updatedText)
        {
            return DiffUtilities.BuildDiffWriteResult(originalText, updatedText);
        }

        private static List<string> TokenizeForDiff(string text)
        {
            return DiffUtilities.TokenizeForDiff(text);
        }

        private static bool[] GetCommonUpdatedTokenFlags(IReadOnlyList<string> originalTokens, IReadOnlyList<string> updatedTokens)
        {
            return DiffUtilities.GetCommonUpdatedTokenFlags(originalTokens, updatedTokens);
        }

        private static Brush CreateWriteSentenceHighlightBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(0x80, 0x6A, 0x6A, 0x6A));
            brush.Freeze();
            return brush;
        }

        private void RenderWriteOutput(LanguageToolWriteResult result, int? caretOffset = null)
        {
            _lastWriteOutputText = result.CorrectedText;

            var paragraph = new Paragraph { Margin = new Thickness(0) };

            foreach (var segment in result.Segments)
            {
                var segmentText = segment.Text ?? string.Empty;
                AddWriteSegmentRun(paragraph, segment, segmentText);
            }

            var document = RichTextDocumentHelper.CreateDocument(TxtWriteOutput, paragraph);

            _suppressWriteOutputFormatting = true;
            try
            {
                TxtWriteOutput.Document = document;
                if (caretOffset.HasValue)
                {
                    var pointer = GetTextPointerAtOffset(document.ContentStart, Math.Min(caretOffset.Value, _lastWriteOutputText.Length));
                    TxtWriteOutput.CaretPosition = pointer;
                }

                RefreshWriteSentenceHighlight();
                RefreshWriteInputHighlight();
            }
            finally
            {
                _suppressWriteOutputFormatting = false;
            }
        }

        private void AddWriteSegmentRun(Paragraph paragraph, LanguageToolSegment segment, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var run = new Run(text);
            if (segment.IsChanged)
            {
                run.TextDecorations = TextDecorations.Underline;
                run.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
            }

            paragraph.Inlines.Add(run);
        }

        private IEnumerable<Run> GetWriteOutputRuns()
        {
            if (TxtWriteOutput.Document?.Blocks.FirstBlock is not Paragraph paragraph)
                yield break;

            foreach (var run in paragraph.Inlines.OfType<Run>())
                yield return run;
        }

        private void RefreshWriteSentenceHighlight()
        {
            if (TxtWriteOutput.Document == null)
                return;

            bool restoreSuppressState = _suppressWriteOutputFormatting;
            _suppressWriteOutputFormatting = true;

            try
            {
                foreach (var run in GetWriteOutputRuns())
                    run.ClearValue(TextElement.BackgroundProperty);

                GetActiveWriteSentenceRange(out var start, out var length);
                if (start < 0 || length <= 0 || string.IsNullOrEmpty(_lastWriteOutputText))
                    return;

                var contentStart = TxtWriteOutput.Document.ContentStart;
                var selectionStart = GetTextPointerAtOffset(contentStart, Math.Clamp(start, 0, _lastWriteOutputText.Length));
                var selectionEnd = GetTextPointerAtOffset(contentStart, Math.Clamp(start + length, 0, _lastWriteOutputText.Length));

                if (selectionStart.CompareTo(selectionEnd) < 0)
                    new TextRange(selectionStart, selectionEnd).ApplyPropertyValue(TextElement.BackgroundProperty, WriteSentenceHighlightBrush);
            }
            finally
            {
                _suppressWriteOutputFormatting = restoreSuppressState;
            }
        }

        private void RefreshWriteInputHighlight()
        {
            if (TxtWriteInput == null)
                return;

            GetActiveWriteSentenceRange(out var outputStart, out _);
            if (outputStart < 0 || string.IsNullOrWhiteSpace(_lastWriteOutputText))
            {
                ClearWriteInputHighlight();
                return;
            }

            var inputText = TxtWriteInput.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(inputText)
                || !TryGetSentenceOrdinal(_lastWriteOutputText, outputStart, out var sentenceOrdinal)
                || !TryGetSentenceRangeByOrdinal(inputText, sentenceOrdinal, out var inputStart, out var inputLength))
            {
                ClearWriteInputHighlight();
                return;
            }

            if (!_writeInputHighlightActive)
            {
                _writeInputSelectionStart = TxtWriteInput.SelectionStart;
                _writeInputSelectionLength = TxtWriteInput.SelectionLength;
            }

            _writeInputHighlightActive = true;

            if (TxtWriteInput.SelectionStart != inputStart || TxtWriteInput.SelectionLength != inputLength)
                TxtWriteInput.Select(inputStart, inputLength);
        }

        private void ClearWriteInputHighlight()
        {
            if (TxtWriteInput == null || !_writeInputHighlightActive)
                return;

            _writeInputHighlightActive = false;
            TxtWriteInput.Select(
                Math.Clamp(_writeInputSelectionStart, 0, TxtWriteInput.Text?.Length ?? 0),
                Math.Clamp(_writeInputSelectionLength, 0, Math.Max(0, (TxtWriteInput.Text?.Length ?? 0) - Math.Clamp(_writeInputSelectionStart, 0, TxtWriteInput.Text?.Length ?? 0))));
        }

        private void ClearWriteOutput()
        {
            ResetWriteSentenceInteraction();
            RenderWriteOutput(new LanguageToolWriteResult { CorrectedText = string.Empty, Segments = new List<LanguageToolSegment> { new() { Text = string.Empty } } });
        }

        private static string GetRichTextBoxText(RichTextBox richTextBox)
        {
            var text = new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd).Text;
            return text.TrimEnd('\r', '\n');
        }

        private static int GetCaretOffset(RichTextBox richTextBox)
        {
            return new TextRange(richTextBox.Document.ContentStart, richTextBox.CaretPosition).Text.Length;
        }

        private static TextPointer GetTextPointerAtOffset(TextPointer start, int characterOffset)
        {
            TextPointer? navigator = start;
            int chars = 0;

            while (navigator is TextPointer current)
            {
                if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var textRun = current.GetTextInRun(LogicalDirection.Forward);
                    if (chars + textRun.Length >= characterOffset)
                        return current.GetPositionAtOffset(characterOffset - chars) ?? current;

                    chars += textRun.Length;
                    navigator = current.GetPositionAtOffset(textRun.Length);
                }
                else
                {
                    var next = current.GetNextContextPosition(LogicalDirection.Forward);
                    if (next == null)
                        break;

                    navigator = next;
                }
            }

            return start.DocumentEnd;
        }

        private void GetActiveWriteSentenceRange(out int start, out int length)
        {
            if (_pinnedWriteSentenceStart >= 0 && _pinnedWriteSentenceLength > 0)
            {
                start = _pinnedWriteSentenceStart;
                length = _pinnedWriteSentenceLength;
                return;
            }

            start = _hoveredWriteSentenceStart;
            length = _hoveredWriteSentenceLength;
        }

        private void ResetWriteSentenceInteraction()
        {
            bool hadActiveInteraction = _hoveredWriteSentenceStart >= 0
                || _pinnedWriteSentenceStart >= 0
                || WriteSentencePopup?.IsOpen == true;

            _hoveredWriteSentenceStart = -1;
            _hoveredWriteSentenceLength = 0;
            _pinnedWriteSentenceStart = -1;
            _pinnedWriteSentenceLength = 0;

            if (WriteSentencePopup != null)
                WriteSentencePopup.IsOpen = false;

            if (hadActiveInteraction)
            {
                RefreshWriteSentenceHighlight();
                ClearWriteInputHighlight();
            }
        }

        private void SetHoveredWriteSentence(int start, int length)
        {
            if (_hoveredWriteSentenceStart == start && _hoveredWriteSentenceLength == length)
                return;

            _hoveredWriteSentenceStart = start;
            _hoveredWriteSentenceLength = length;
            RefreshWriteSentenceHighlight();
            RefreshWriteInputHighlight();
        }

        private void ClearHoveredWriteSentence()
        {
            if (_hoveredWriteSentenceStart < 0 || WriteSentencePopup.IsOpen)
                return;

            _hoveredWriteSentenceStart = -1;
            _hoveredWriteSentenceLength = 0;
            RefreshWriteSentenceHighlight();
            ClearWriteInputHighlight();
        }

        private void PinWriteSentence(int start, int length)
        {
            _pinnedWriteSentenceStart = start;
            _pinnedWriteSentenceLength = length;
            _hoveredWriteSentenceStart = start;
            _hoveredWriteSentenceLength = length;
            RefreshWriteSentenceHighlight();
            RefreshWriteInputHighlight();
        }

        private void RefreshWriteHoverFromMousePosition()
        {
            try
            {
                if (WriteSentencePopup.IsOpen || _pinnedWriteSentenceStart >= 0 || !TxtWriteOutput.IsMouseOver)
                    return;

                var point = Mouse.GetPosition(TxtWriteOutput);
                if (point.X < 0 || point.Y < 0 || point.X > TxtWriteOutput.ActualWidth || point.Y > TxtWriteOutput.ActualHeight)
                {
                    ClearHoveredWriteSentence();
                    return;
                }

                if (TryGetSentenceRangeAtPoint(point, out var start, out var length))
                    SetHoveredWriteSentence(start, length);
                else
                    ClearHoveredWriteSentence();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void SelectWriteSentence(int start, int length)
        {
            var selectionStart = GetTextPointerAtOffset(TxtWriteOutput.Document.ContentStart, Math.Max(0, start));
            var selectionEnd = GetTextPointerAtOffset(TxtWriteOutput.Document.ContentStart, Math.Min(start + length, _lastWriteOutputText.Length));
            TxtWriteOutput.Selection.Select(selectionStart, selectionEnd);
            TxtWriteOutput.Focus();
        }

        private bool TryGetSentenceRangeAtPoint(Point point, out int start, out int length)
        {
            start = -1;
            length = 0;

            var text = GetRichTextBoxText(TxtWriteOutput);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            TextPointer? pointer;
            try
            {
                pointer = TxtWriteOutput.GetPositionFromPoint(point, true);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (pointer == null)
                return false;

            int offset = new TextRange(TxtWriteOutput.Document.ContentStart, pointer).Text.Length;
            offset = Math.Clamp(offset, 0, Math.Max(0, text.Length - 1));

            if (char.IsWhiteSpace(text[offset]))
            {
                int left = offset;
                while (left > 0 && char.IsWhiteSpace(text[left]) && text[left] != '\r' && text[left] != '\n')
                    left--;

                if (!char.IsWhiteSpace(text[left]))
                {
                    offset = left;
                }
                else
                {
                    int right = offset;
                    while (right < text.Length && char.IsWhiteSpace(text[right]) && text[right] != '\r' && text[right] != '\n')
                        right++;

                    if (right >= text.Length)
                        return false;

                    offset = right;
                }
            }

            return TryGetSentenceRange(text, offset, out start, out length);
        }

        private static bool TryGetSentenceRange(string text, int offset, out int start, out int length)
        {
            return SentenceUtilities.TryGetSentenceRange(text, offset, out start, out length);
        }

        private static bool TryGetSentenceOrdinal(string text, int offset, out int ordinal)
        {
            return SentenceUtilities.TryGetSentenceOrdinal(text, offset, out ordinal);
        }

        private static bool TryGetSentenceRangeByOrdinal(string text, int ordinal, out int start, out int length)
        {
            return SentenceUtilities.TryGetSentenceRangeByOrdinal(text, ordinal, out start, out length);
        }

        private static bool TryGetSentenceRangeByCursor(string text, ref int cursor, out int start, out int length)
        {
            return SentenceUtilities.TryGetSentenceRangeByCursor(text, ref cursor, out start, out length);
        }

        private static bool IsSentenceBoundary(char c)
        {
            return SentenceUtilities.IsSentenceBoundary(c);
        }

        private void OpenWriteSentencePopup(Point point)
        {
            WriteSentencePopup.HorizontalOffset = Math.Max(0, point.X - 8);
            WriteSentencePopup.VerticalOffset = Math.Max(0, point.Y - 42);
            WriteSentencePopup.IsOpen = true;
        }

        internal async Task<(string translatedText, string effectiveSource, string effectiveTarget)> TranslateForFloatingAsync(string text, string targetCode)
        {
            var effectiveSource = await DetectSourceLanguageAsync(text) ?? "auto";
            var effectiveTarget = targetCode;

            if (effectiveSource.Equals(effectiveTarget, StringComparison.OrdinalIgnoreCase))
            {
                if (!EnsureDifferentTargetForFloating(effectiveSource, effectiveTarget, out effectiveTarget))
                    throw new InvalidOperationException("Choose different languages.");
            }
            else
            {
                SetCurrentTargetCode(effectiveTarget);
            }

            var translatedText = await TranslateTextAsync(text, effectiveSource, effectiveTarget);
            AddRecentTranslation(text, translatedText, effectiveSource, effectiveTarget);
            return (translatedText, effectiveSource, effectiveTarget);
        }

        private async Task DoTranslateAsync(bool showErrors = true, CancellationToken cancellationToken = default)
        {
            var text = (TxtInput.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                TxtOutput.Clear();
                SetStatus("Nothing to translate.");
                return;
            }

            if (CmbTarget.SelectedItem is not LtLanguage target)
            {
                SetStatus("Select a target language.");
                return;
            }

            var selectedSource = (CmbSource.SelectedItem as LtLanguage)?.code ?? "auto";
            var targetLang = target.code;

            BtnTranslate.IsEnabled = false;
            SetStatus("Translating...");

            try
            {
                var effectiveSource = selectedSource;
                if (selectedSource.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    var detected = await _translationService.DetectSourceLanguageAsync(text, cancellationToken);
                    if (!string.IsNullOrEmpty(detected))
                    {
                        effectiveSource = detected;
                        SetStatus($"Detected: {detected.ToUpperInvariant()} → {targetLang.ToUpperInvariant()}");
                    }
                }

                // Silent auto-change of target if same as source
                if (effectiveSource.Equals(targetLang, StringComparison.OrdinalIgnoreCase))
                {
                    if (!EnsureDifferentTarget(effectiveSource, out targetLang))
                    {
                        SetStatus("Choose different languages.");
                        return;
                    }
                }

                TxtOutput.Text = await _translationService.TranslateTextAsync(text, effectiveSource, targetLang, cancellationToken);
                AddRecentTranslation(text, TxtOutput.Text ?? string.Empty, effectiveSource, targetLang);
                SetStatus("Done.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Translation canceled.");
            }
            catch (Exception ex)
            {
                SetStatus("Error during translation.");
                AppLogger.Log(ex, "DoTranslateAsync failed");
                if (showErrors)
                    ShowRetryMessage("Translation failed", ex.Message, async () => await DoTranslateAsync(showErrors, CancellationToken.None));
            }
            finally
            {
                BtnTranslate.IsEnabled = true;
            }
        }

        private async Task DoWriteAsync(bool showErrors = true, CancellationToken cancellationToken = default)
        {
            var text = (TxtWriteInput.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                ClearWriteOutput();
                SetStatus("Nothing to improve.");
                return;
            }

            BtnWrite.IsEnabled = false;
            ResetWriteSentenceInteraction();
            SetStatus("Improving text...");

            try
            {
                var improved = await _writingService.ImproveWritingAsync(text, cancellationToken);
                RenderWriteOutput(BuildDiffWriteResult(text, improved.CorrectedText));
                AddRecentWrite(text, improved.CorrectedText);
                SetStatus(improved.CorrectedText == text ? "No suggestions found." : "Write done.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Writing check canceled.");
            }
            catch (Exception ex)
            {
                SetStatus("Error during writing check.");
                AppLogger.Log(ex, "DoWriteAsync failed");
                if (showErrors)
                    ShowRetryMessage("Writing check failed", ex.Message, async () => await DoWriteAsync(showErrors, CancellationToken.None));
            }
            finally
            {
                BtnWrite.IsEnabled = true;
            }
        }

        private async Task DoRewriteWriteOutputAsync(int? rangeStart = null, int? rangeLength = null, bool showErrors = true, CancellationToken cancellationToken = default)
        {
            var currentOutput = GetRichTextBoxText(TxtWriteOutput);
            if (string.IsNullOrWhiteSpace(currentOutput))
            {
                SetStatus("Nothing to rephrase.");
                return;
            }

            int selectionStart = 0;
            int selectionLength = 0;
            bool hasSelection = false;

            if (rangeStart.HasValue && rangeLength.HasValue && rangeLength.Value > 0)
            {
                selectionStart = Math.Clamp(rangeStart.Value, 0, currentOutput.Length);
                selectionLength = Math.Min(rangeLength.Value, Math.Max(0, currentOutput.Length - selectionStart));
                hasSelection = selectionLength > 0;
            }
            else
            {
                var selection = TxtWriteOutput.Selection;
                var selectedRange = new TextRange(selection.Start, selection.End);
                var selectedText = selectedRange.Text;
                hasSelection = !string.IsNullOrWhiteSpace(selectedText);
                if (hasSelection)
                {
                    selectionStart = Math.Min(new TextRange(TxtWriteOutput.Document.ContentStart, selection.Start).Text.Length, currentOutput.Length);
                    selectionLength = Math.Min(selectedText.Length, Math.Max(0, currentOutput.Length - selectionStart));
                }
            }

            var sourceText = hasSelection
                ? currentOutput.Substring(selectionStart, selectionLength)
                : currentOutput;

            BtnRewriteWriteOutput.IsEnabled = false;
            SetStatus(hasSelection ? "Rephrasing selection..." : "Rephrasing text...");

            try
            {
                var rewrittenText = await RewriteTextAsync(sourceText, cancellationToken);
                if (string.IsNullOrWhiteSpace(rewrittenText))
                    rewrittenText = sourceText;

                string updatedOutput = hasSelection
                    ? currentOutput.Remove(selectionStart, selectionLength).Insert(selectionStart, rewrittenText)
                    : rewrittenText;

                RenderWriteOutput(BuildDiffWriteResult(TxtWriteInput.Text ?? string.Empty, updatedOutput));
                AddRecentWrite(TxtWriteInput.Text ?? string.Empty, updatedOutput);
                _ = Dispatcher.BeginInvoke(new Action(RefreshWriteHoverFromMousePosition), System.Windows.Threading.DispatcherPriority.Background);
                SetStatus(hasSelection ? "Selection rephrased." : "Text rephrased.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Rephrasing canceled.");
            }
            catch (Exception ex)
            {
                SetStatus("Error during rephrasing.");
                AppLogger.Log(ex, "DoRewriteWriteOutputAsync failed");
                if (showErrors)
                    ShowRetryMessage("Rewrite failed", ex.Message, async () => await DoRewriteWriteOutputAsync(rangeStart, rangeLength, showErrors, CancellationToken.None));
            }
            finally
            {
                BtnRewriteWriteOutput.IsEnabled = true;
            }
        }

        private async Task<string> RewriteTextAsync(string text, CancellationToken cancellationToken = default)
        {
            return await _writingService.RewriteTextAsync(text, _languages.ToList(), cancellationToken);
        }

        private string GetRewritePivotLanguage(string sourceLanguage)
        {
            string[] preferredLanguages = sourceLanguage.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? new[] { "de", "fr", "es" }
                : new[] { "en", "de", "fr", "es" };

            foreach (var language in preferredLanguages)
            {
                if (!language.Equals(sourceLanguage, StringComparison.OrdinalIgnoreCase) && IsTranslationLanguageAvailable(language))
                    return language;
            }

            foreach (var language in _languages)
            {
                if (!language.code.Equals(sourceLanguage, StringComparison.OrdinalIgnoreCase))
                    return language.code;
            }

            throw new InvalidOperationException("No rewrite pivot language available.");
        }

        private bool IsTranslationLanguageAvailable(string code)
        {
            if (_languages.Count == 0)
                return true;

            return _languages.Any(x => x.code.Equals(code, StringComparison.OrdinalIgnoreCase));
        }

        // Global: Ctrl+Shift+C — copy selection, bring app to front, paste, translate
        private async Task CopySelectionAndTranslateAsync()
        {
            try
            {
                // If this app is foreground, nothing to copy
                var fg = _nativeInputService.GetForegroundWindowHandle();
                var myHandle = new WindowInteropHelper(this).Handle;
                if (fg == myHandle)
                {
                    SetStatus("This app has focus; select text in another app first.");
                    return;
                }

                // Try UI Automation first (works for many apps including browsers and editors)
                try
                {
                    var selectedViaUIA = NativeInputService.TryGetSelectedTextViaUIAutomation();
                    if (!string.IsNullOrWhiteSpace(selectedViaUIA))
                    {
                        SetStatus("Captured selection via UI Automation.");
                        await HandleGlobalHotkeySelectionAsync(fg, selectedViaUIA!);
                        return;
                    }
                }
                catch { /* ignore and fall back to Ctrl+C */ }

                SetStatus("Sending Ctrl+C to active window...");
                bool sent = await _nativeInputService.PressCtrlCAsync();
                SetStatus(sent ? "Ctrl+C sent, polling clipboard..." : "Ctrl+C send failed, polling clipboard anyway...");

                // Floating icon removed

                // Poll clipboard up to ~2000ms until non-empty text appears
                string? text = null;
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(100);
                    try
                    {
                        if (global::System.Windows.Clipboard.ContainsText())
                        {
                            var candidate = global::System.Windows.Clipboard.GetText();
                            if (!string.IsNullOrWhiteSpace(candidate))
                            {
                                text = candidate;
                                break;
                            }
                        }
                    }
                    catch { /* clipboard might be busy */ }
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    SetStatus("No text captured from selection.");
                    return;
                }

                await HandleGlobalHotkeySelectionAsync(fg, text);
            }
            catch (Exception ex)
            {
                SetStatus("Global send-to-app failed.");
                global::System.Windows.MessageBox.Show(this, "Global hotkey error:\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task HandleGlobalHotkeySelectionAsync(IntPtr sourceWindowHandle, string text)
        {
            _lastCapturedWindowHandle = sourceWindowHandle;
            _lastCapturedProcessId = _nativeInputService.GetProcessIdForWindow(sourceWindowHandle);
            Point? launchPoint = null;
            if (_nativeInputService.TryGetCursorPos(out var cursorPoint))
                launchPoint = new Point(cursorPoint.X, cursorPoint.Y);

            if (_settings.HotkeyOpensMainWindow)
            {
                ShowWindowFromTray();
                _suppressLiveTranslation = true;
                TxtInput.Text = text;
                _suppressLiveTranslation = false;
                await DoTranslateAsync();
                return;
            }

            await EnsureFloatingTranslateWindow().ShowTranslationAsync(text, false, launchPoint);
        }

        private static async Task<bool> PressCtrlCAsync()
        {
            // Try to target the current foreground window by attaching thread input, send Ctrl+C, then detach.
            var fg = GetForegroundWindow();
            uint fgThread = 0;
            if (fg != IntPtr.Zero)
                fgThread = GetWindowThreadProcessId(fg, out _);

            uint curThread = GetCurrentThreadId();
            bool attached = false;
            try
            {
                if (fgThread != 0 && AttachThreadInput(curThread, fgThread, true))
                    attached = true;

                // Try to send WM_COPY to the focused control first (works for many standard controls)
                if (fg != IntPtr.Zero)
                {
                    SetForegroundWindow(fg);
                    var gti = new GUITHREADINFO();
                    gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                    try
                    {
                        if (GetGUIThreadInfo(fgThread, ref gti) && gti.hwndFocus != IntPtr.Zero)
                        {
                            // Send WM_COPY directly to focused control
                            SendMessage(gti.hwndFocus, WM_COPY, IntPtr.Zero, IntPtr.Zero);
                            await Task.Delay(120);
                            return true;
                        }
                    }
                    catch { /* ignore and fallback */ }
                }

                // Fallback: send synthetic Ctrl+C to the foreground window
                var inputs = new INPUT[]
                {
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL_KEY } } },
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_C_KEY } } },
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_C_KEY, dwFlags = KEYEVENTF_KEYUP } } },
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL_KEY, dwFlags = KEYEVENTF_KEYUP } } },
                };
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));

                // Wait a small amount of time to let target app process the synthetic input
                await Task.Delay(180);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (attached)
                    AttachThreadInput(curThread, fgThread, false);
            }
        }

        private static async Task<bool> SendCtrlShortcutAsync(IntPtr targetWindowHandle, ushort key)
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

                var inputs = new INPUT[]
                {
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL_KEY } } },
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = key } } },
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = key, dwFlags = KEYEVENTF_KEYUP } } },
                    new INPUT { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL_KEY, dwFlags = KEYEVENTF_KEYUP } } },
                };

                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                await Task.Delay(120);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThread, targetThread, false);
            }
        }

        private void Combo_DropDownOpened(object? sender, EventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox cb)
            {
                // Align popup width and placement to the combo box width and right edge
                try
                {
                    var popup = cb.Template.FindName("PART_Popup", cb) as Popup;
                    if (popup != null && popup.Child is FrameworkElement child)
                    {
                        // Ensure child measured
                        child.Measure(new global::System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                        double childWidth = child.DesiredSize.Width;
                        double comboWidth = cb.ActualWidth;
                        double desiredWidth = Math.Max(comboWidth, childWidth);
                        // set popup child width so dropdown aligns
                        child.Width = desiredWidth;
                        // Offset so right edges align
                        popup.HorizontalOffset = comboWidth - desiredWidth;
                    }
                }
                catch { }
            }
        }

        private void AdjustComboWidth(System.Windows.Controls.ComboBox combo, List<string> items)
        {
            try
            {
                // Measure text width of longest item and set combo width to that + padding
                double max = 0;
                var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                var tf = new System.Windows.Media.Typeface(combo.FontFamily, combo.FontStyle, combo.FontWeight, combo.FontStretch);
                foreach (var it in items)
                {
                    var ft = new System.Windows.Media.FormattedText(it, System.Globalization.CultureInfo.CurrentCulture, global::System.Windows.FlowDirection.LeftToRight,
                        tf, combo.FontSize, System.Windows.Media.Brushes.Black, dpi);
                    if (ft.Width > max) max = ft.Width;
                }

                // Leave a small padding for left/right and room for the drop-down toggle
                const double extra = 36.0; // padding + toggle button
                double desired = Math.Ceiling(max + extra);
                // Respect min/max if set on control
                double min = combo.MinWidth > 0 ? combo.MinWidth : 80;
                double maxAllowed = combo.MaxWidth > 0 ? combo.MaxWidth : 600;
                double final = Math.Min(Math.Max(min, desired), maxAllowed);
                combo.Width = final;
            }
            catch { }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc, int hookType)
        {
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            IntPtr moduleHandle = GetModuleHandle(curModule?.ModuleName ?? string.Empty);
            return SetWindowsHookEx(hookType, proc, moduleHandle, 0);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONUP)
                Dispatcher.InvokeAsync(async () => await ShowSelectionLauncherAsync());

            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                try
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    // Check configured modifiers
                    bool modsOk = true;
                    if (_settings.CtrlRequired)
                        modsOk &= (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    if (_settings.AltRequired)
                        modsOk &= (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                    if (_settings.ShiftRequired)
                        modsOk &= (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

                    if (modsOk && vkCode == _settings.HotkeyVk)
                    {
                        var now = DateTime.UtcNow;
                        if (now - _lastHotkey <= TimeSpan.FromMilliseconds(_settings.ThresholdMs))
                        {
                            // Double-tap detected - run capture on UI thread
                            Dispatcher.Invoke(() => SetStatus($"Double {_settings.GetDisplayString()} detected: capturing selection..."));
                            // Ensure CopySelectionAndTranslateAsync runs on the UI thread
                            Dispatcher.InvokeAsync(async () => await CopySelectionAndTranslateAsync());
                            _lastHotkey = DateTime.MinValue;
                        }
                        else
                        {
                            _lastHotkey = now;
                        }
                    }
                }
                catch { }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // Button handlers
        private async void BtnTranslate_Click(object sender, RoutedEventArgs e) => await DoTranslateAsync();

        private async void BtnOcrCapture_Click(object sender, RoutedEventArgs e)
        {
            await CaptureRegionAndTranslateAsync();
        }

        internal async Task<string?> CaptureTextFromScreenRegionAsync(bool restoreMainWindow)
        {
            Rect bounds;

            try
            {
                if (restoreMainWindow)
                {
                    SetStatus("Select a screen region for OCR.");
                    Hide();
                }

                await Task.Delay(150);

                var selector = new ScreenRegionSelectionWindow();
                if (selector.ShowDialog() != true)
                {
                    if (restoreMainWindow)
                    {
                        ShowWindowFromTray();
                        SetStatus("OCR capture canceled.");
                    }

                    return null;
                }

                bounds = selector.SelectedBounds;
                if (bounds.Width < 2 || bounds.Height < 2)
                {
                    if (restoreMainWindow)
                    {
                        ShowWindowFromTray();
                        SetStatus("OCR capture canceled.");
                    }

                    return null;
                }
            }
            catch (Exception ex)
            {
                if (restoreMainWindow)
                {
                    ShowWindowFromTray();
                    SetStatus("Failed to start OCR capture.");
                }

                AppLogger.Log(ex, "CaptureTextFromScreenRegionAsync selection failed");
                return null;
            }

            try
            {
                await Task.Delay(75);

                using var bitmap = new DrawingBitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), DrawingPixelFormat.Format32bppArgb);
                using (var graphics = DrawingGraphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        (int)Math.Floor(bounds.X),
                        (int)Math.Floor(bounds.Y),
                        0,
                        0,
                        bitmap.Size,
                        System.Drawing.CopyPixelOperation.SourceCopy);
                }

                if (restoreMainWindow)
                {
                    ShowWindowFromTray();
                    SetStatus("Running OCR...");
                }

                var text = await _ocrService.RecognizeTextAsync(bitmap);
                if (string.IsNullOrWhiteSpace(text))
                {
                    if (restoreMainWindow)
                        SetStatus("No text recognized from screenshot.");

                    return null;
                }

                return text.Trim();
            }
            catch (Exception ex)
            {
                if (restoreMainWindow)
                {
                    ShowWindowFromTray();
                    SetStatus("OCR failed.");
                }

                AppLogger.Log(ex, "CaptureTextFromScreenRegionAsync OCR failed");
                return null;
            }
        }

        private async Task CaptureRegionAndTranslateAsync()
        {
            var text = await CaptureTextFromScreenRegionAsync(true);
            if (string.IsNullOrWhiteSpace(text))
                return;

            TabsMain.SelectedIndex = 0;
            _suppressLiveTranslation = true;
            TxtInput.Text = text;
            _suppressLiveTranslation = false;
            await DoTranslateAsync();
        }

        private void QueueLiveTranslation()
        {
            if (_suppressLiveTranslation || !IsLoaded)
                return;

            _liveTranslateTimer.Stop();
            _liveTranslateTimer.Start();
        }

        private void QueueLiveWriting()
        {
            if (_suppressLiveWriting || !IsLoaded)
                return;

            _liveWriteTimer.Stop();
            _liveWriteTimer.Start();
        }

        private async void LiveTranslateTimer_Tick(object? sender, EventArgs e)
        {
            _liveTranslateTimer.Stop();

            if (_suppressLiveTranslation || !IsLoaded)
                return;

            _liveTranslateCts?.Cancel();
            _liveTranslateCts = new CancellationTokenSource();
            await DoTranslateAsync(false, _liveTranslateCts.Token);
        }

        private async void LiveWriteTimer_Tick(object? sender, EventArgs e)
        {
            _liveWriteTimer.Stop();

            if (_suppressLiveWriting || !IsLoaded)
                return;

            _liveWriteCts?.Cancel();
            _liveWriteCts = new CancellationTokenSource();
            await DoWriteAsync(false, _liveWriteCts.Token);
        }

        private async void BtnRewriteWriteOutput_Click(object sender, RoutedEventArgs e) => await DoRewriteWriteOutputAsync();

        private FloatingTranslateWindow EnsureFloatingTranslateWindow()
        {
            _floatingTranslateWindow ??= new FloatingTranslateWindow(this);
            return _floatingTranslateWindow;
        }

        private bool IsOwnWindowHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return false;

            if (handle == new WindowInteropHelper(this).Handle)
                return true;

            if (_floatingTranslateWindow != null && _floatingTranslateWindow.IsVisible)
            {
                var floatingHandle = new WindowInteropHelper(_floatingTranslateWindow).Handle;
                if (handle == floatingHandle)
                    return true;
            }

            return false;
        }

        private async Task ShowSelectionLauncherAsync()
        {
            try
            {
                if (!_nativeInputService.TryGetCursorPos(out var point))
                    return;

                if (_floatingTranslateWindow?.ContainsScreenPoint(new Point(point.X, point.Y)) == true)
                    return;

                var foreground = _nativeInputService.GetForegroundWindowHandle();
                if (IsOwnWindowHandle(foreground))
                    return;

                await Task.Delay(80);
                var selectedText = await CaptureSelectedTextFromWindowAsync(foreground, allowClipboardFallback: false);
                if (string.IsNullOrWhiteSpace(selectedText))
                    return;

                _lastCapturedWindowHandle = foreground;
                _lastCapturedProcessId = _nativeInputService.GetProcessIdForWindow(foreground);

                var floatingWindow = EnsureFloatingTranslateWindow();
                if (floatingWindow.IsExpandedMode)
                    await floatingWindow.ShowCurrentModeAsync(selectedText, false, new Point(point.X, point.Y));
                else
                    floatingWindow.ShowLauncher(selectedText, new Point(point.X, point.Y));
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "ShowSelectionLauncherAsync failed");
            }
        }

        internal IReadOnlyList<LtLanguage> GetTranslationLanguages() => GetOrderedLanguages();

        internal string GetCurrentTargetCode() => (CmbTarget.SelectedItem as LtLanguage)?.code ?? "en";

        internal void SetCurrentTargetCode(string code)
        {
            int idx = FindIndexByCode(CmbTarget.Items, code);
            if (idx >= 0)
                CmbTarget.SelectedIndex = idx;

            RefreshFavoriteTargetButton();
        }

        internal string GetLanguageDisplayName(string code)
        {
            if (code.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return "Auto-detect";

            var language = _languages.FirstOrDefault(x => x.code.Equals(code, StringComparison.OrdinalIgnoreCase));
            return language?.name ?? code.ToUpperInvariant();
        }

        internal FontFamily GetEditorFontFamily()
        {
            return new FontFamily(string.IsNullOrWhiteSpace(_settings.EditorFontFamily) ? "Arial" : _settings.EditorFontFamily);
        }

        internal double GetEditorFontSize()
        {
            return _settings.EditorFontSize >= 8 ? _settings.EditorFontSize : 20;
        }

        internal void OpenSettingsDialog()
        {
            try
            {
                var previousServerUrl = _settings.ServerUrl;
                var win = new SettingsWindow(_settings);
                win.Owner = this;
                if (win.ShowDialog() == true)
                {
                    _settings = AppSettings.Load();
                    if (!WindowsStartupHelper.TrySetEnabled(_settings.StartWithWindows, out var startupError))
                    {
                        _settings.StartWithWindows = WindowsStartupHelper.IsEnabled();
                        _settings.Save();
                        throw new InvalidOperationException("Failed to update Windows startup registration. " + startupError);
                    }

                    AppLogger.Configure(_settings.LogLevel);
                    ConfigureApiClient();
                    ApplyTheme(_settings.IsDarkMode);
                    ApplyEditorSettings();
                    ApplyShortcutSettings();
                    RefreshLanguageSources();
                    RefreshRecentHistoryViews();

                    if (!string.Equals(previousServerUrl, _settings.ServerUrl, StringComparison.OrdinalIgnoreCase))
                        _ = Dispatcher.InvokeAsync(async () => await InitializeAsync());

                    SetStatus($"Settings updated: {_settings.GetDisplayString()} (DarkMode={_settings.IsDarkMode})");
                    AppLogger.Log("Settings updated.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "OpenSettingsDialog failed");
                global::System.Windows.MessageBox.Show(this, "Failed to open settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        internal void OpenMainWindowWithTranslation(string sourceText, string translatedText, string targetCode)
        {
            SetCurrentTargetCode(targetCode);
            TabsMain.SelectedIndex = 0;
            ShowWindowFromTray();
            _suppressLiveTranslation = true;
            TxtInput.Text = sourceText;
            TxtOutput.Text = translatedText;
            _suppressLiveTranslation = false;
            SetStatus("Opened translation in main window.");
        }

        internal void OpenMainWindowWithWrite(string sourceText, string correctedText)
        {
            TabsMain.SelectedIndex = 1;
            ShowWindowFromTray();
            _suppressLiveWriting = true;
            TxtWriteInput.Text = sourceText;
            RenderWriteOutput(BuildDiffWriteResult(sourceText, correctedText));
            _suppressLiveWriting = false;
            SetStatus("Opened write result in main window.");
        }

        internal async Task<string> ImproveWritingForFloatingAsync(string text)
        {
            var result = await ImproveWritingDetailedForFloatingAsync(text);
            return result.CorrectedText;
        }

        internal async Task<LanguageToolWriteResult> ImproveWritingDetailedForFloatingAsync(string text)
        {
            var result = await ImproveWritingAsync(text);
            AddRecentWrite(text, result.CorrectedText);
            return result;
        }

        internal async Task<bool> ReplaceLastCapturedTextAsync(string translatedText)
        {
            if (string.IsNullOrWhiteSpace(translatedText) || _lastCapturedWindowHandle == IntPtr.Zero)
                return false;

            global::System.Windows.Clipboard.SetText(translatedText);
            return await _nativeInputService.SendCtrlShortcutAsync(_lastCapturedWindowHandle, NativeInputService.VK_V_KEY);
        }

        internal async Task<string?> CaptureSelectionFromLastWindowAsync()
        {
            if (_lastCapturedWindowHandle == IntPtr.Zero)
                return null;

            return await CaptureSelectionFromWindowClipboardAsync(_lastCapturedWindowHandle);
        }

        private void ConfigureApiClient()
        {
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsDialog();
        }

        private async void BtnWrite_Click(object sender, RoutedEventArgs e)
        {
            await DoWriteAsync();
        }

        private void BtnFavoriteTarget_Click(object sender, RoutedEventArgs e)
        {
            if (CmbTarget.SelectedItem is not LtLanguage target)
                return;

            var existing = _settings.FavoriteTargetLanguages.FirstOrDefault(x => x.Equals(target.code, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                _settings.FavoriteTargetLanguages.Remove(existing);
            else
                _settings.FavoriteTargetLanguages.Add(target.code);

            _settings.Save();
            RefreshLanguageSources();
            _floatingTranslateWindow?.RefreshLanguages();
        }

        private void BtnSaveOutput_Click(object sender, RoutedEventArgs e)
        {
            SaveTextToFile(TxtOutput.Text ?? string.Empty, "Save translation output");
        }

        private void BtnSaveWriteOutput_Click(object sender, RoutedEventArgs e)
        {
            SaveTextToFile(_lastWriteOutputText, "Save write output");
        }

        private void CmbRecentTranslations_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbRecentTranslations.SelectedItem is not HistoryEntry entry)
                return;

            _suppressLiveTranslation = true;
            if (!string.IsNullOrWhiteSpace(entry.TargetLanguage))
                SetCurrentTargetCode(entry.TargetLanguage);
            TxtInput.Text = entry.SourceText;
            TxtOutput.Text = entry.ResultText;
            _suppressLiveTranslation = false;

            SetStatus("Loaded recent translation.");
            CmbRecentTranslations.SelectedItem = null;
        }

        private void CmbRecentWrites_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbRecentWrites.SelectedItem is not HistoryEntry entry)
                return;

            _suppressLiveWriting = true;
            TxtWriteInput.Text = entry.SourceText;
            RenderWriteOutput(BuildDiffWriteResult(entry.SourceText, entry.ResultText));
            _suppressLiveWriting = false;

            SetStatus("Loaded recent write result.");
            CmbRecentWrites.SelectedItem = null;
        }

        private void BtnCopyOutput_Click(object sender, RoutedEventArgs e)
        {
            var outputText = TxtOutput.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(outputText))
            {
                SetStatus("Nothing to copy.");
                return;
            }

            global::System.Windows.Clipboard.SetText(outputText);
            SetStatus("Output copied.");
        }

        private void BtnCopyWriteOutput_Click(object sender, RoutedEventArgs e)
        {
            var outputText = _lastWriteOutputText;
            if (string.IsNullOrWhiteSpace(outputText))
            {
                SetStatus("Nothing to copy.");
                return;
            }

            global::System.Windows.Clipboard.SetText(outputText);
            SetStatus("Write output copied.");
        }

        private void TxtInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            QueueLiveTranslation();
        }

        private void TxtWriteInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            QueueLiveWriting();
        }

        private void TxtWriteOutput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressWriteOutputFormatting)
                return;

            ResetWriteSentenceInteraction();
            _pendingWriteOutputText = GetRichTextBoxText(TxtWriteOutput);
            _pendingWriteOutputCaretOffset = GetCaretOffset(TxtWriteOutput);

            if (_writeOutputReformatPending)
                return;

            _writeOutputReformatPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _writeOutputReformatPending = false;

                try
                {
                    RenderWriteOutput(
                        BuildDiffWriteResult(TxtWriteInput.Text ?? string.Empty, _pendingWriteOutputText),
                        _pendingWriteOutputCaretOffset);
                }
                catch (InvalidOperationException)
                {
                    SetStatus("Write output update skipped.");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void TxtWriteOutput_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (WriteSentencePopup.IsOpen)
                return;

            if (TryGetSentenceRangeAtPoint(e.GetPosition(TxtWriteOutput), out var start, out var length))
                SetHoveredWriteSentence(start, length);
            else
                ClearHoveredWriteSentence();
        }

        private void TxtWriteOutput_MouseLeave(object sender, MouseEventArgs e)
        {
            ClearHoveredWriteSentence();
        }

        private void TxtWriteOutput_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!TryGetSentenceRangeAtPoint(e.GetPosition(TxtWriteOutput), out var start, out var length))
            {
                ResetWriteSentenceInteraction();
                return;
            }

            var popupPoint = e.GetPosition(TxtWriteOutput);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PinWriteSentence(start, length);
                SelectWriteSentence(start, length);
                OpenWriteSentencePopup(popupPoint);
            }), System.Windows.Threading.DispatcherPriority.Input);

            e.Handled = true;
        }

        private async void BtnRewriteHoveredSentence_Click(object sender, RoutedEventArgs e)
        {
            int start = _pinnedWriteSentenceStart;
            int length = _pinnedWriteSentenceLength;
            WriteSentencePopup.IsOpen = false;
            ResetWriteSentenceInteraction();
            await DoRewriteWriteOutputAsync(start >= 0 ? start : null, length > 0 ? length : null);
        }

        private void BtnCloseWriteSentencePopup_Click(object sender, RoutedEventArgs e)
        {
            ResetWriteSentenceInteraction();
        }

        private void CmbSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            QueueLiveTranslation();
        }

        private void CmbTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshFavoriteTargetButton();
            QueueLiveTranslation();
        }

        private void BtnSwap_Click(object sender, RoutedEventArgs e)
        {
            if (CmbSource.SelectedItem is LtLanguage src && CmbTarget.SelectedItem is LtLanguage tgt)
            {
                if (!src.code.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    int newSrcIndex = FindIndexByCode(CmbSource.Items, tgt.code);
                    int newTgtIndex = FindIndexByCode(CmbTarget.Items, src.code);
                    if (newSrcIndex >= 0 && newTgtIndex >= 0)
                    {
                        CmbSource.SelectedIndex = newSrcIndex;
                        CmbTarget.SelectedIndex = newTgtIndex;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(TxtOutput.Text))
            {
                TxtInput.Text = TxtOutput.Text;
                TxtOutput.Clear();
            }
        }

        // In-app shortcut handlers
        private void TranslateCommand_CanExecute(object? sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;
        private async void TranslateCommand_Executed(object? sender, ExecutedRoutedEventArgs e)
        {
            if (IsWriteTabActive())
                await DoWriteAsync();
            else
                await DoTranslateAsync();
        }

        private void SwapCommand_CanExecute(object? sender, CanExecuteRoutedEventArgs e) => e.CanExecute = CmbSource.SelectedItem != null && CmbTarget.SelectedItem != null;
        private void SwapCommand_Executed(object? sender, ExecutedRoutedEventArgs e) => BtnSwap_Click(sender!, new RoutedEventArgs());

        private void ClearCommand_CanExecute(object? sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !string.IsNullOrEmpty(TxtInput.Text) || !string.IsNullOrEmpty(TxtOutput.Text);
        private void ClearCommand_Executed(object? sender, ExecutedRoutedEventArgs e)
        {
            if (IsWriteTabActive())
            {
                TxtWriteInput.Clear();
                ClearWriteOutput();
            }
            else
            {
                TxtInput.Clear();
                TxtOutput.Clear();
            }

            SetStatus("Cleared.");
        }

        private void PasteAndTranslateCommand_CanExecute(object? sender, CanExecuteRoutedEventArgs e)
        {
            try { e.CanExecute = global::System.Windows.Clipboard.ContainsText(); } catch { e.CanExecute = false; }
        }
        private async void PasteAndTranslateCommand_Executed(object? sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                if (global::System.Windows.Clipboard.ContainsText())
                {
                    TxtInput.Text = global::System.Windows.Clipboard.GetText();
                    await DoTranslateAsync();
                }
            }
            catch { SetStatus("Paste failed."); }
        }

        // Ensure Ctrl+Enter triggers translate even when TextBox has focus
        private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (IsOcrShortcut(e))
            {
                e.Handled = true;
                await CaptureRegionAndTranslateAsync();
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (e.Key == Key.Enter || e.Key == Key.Return))
            {
                e.Handled = true;
                if (IsWriteTabActive())
                    await DoWriteAsync();
                else
                    await DoTranslateAsync();
            }
        }

        private bool IsOcrShortcut(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return false;

            bool modifiersMatch = (!_settings.OcrShortcutCtrlRequired || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                && (!_settings.OcrShortcutAltRequired || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                && (!_settings.OcrShortcutShiftRequired || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                && (!_settings.OcrShortcutWinRequired || (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0);

            if (!modifiersMatch)
                return false;

            int virtualKey = KeyInterop.VirtualKeyFromKey(key);
            return virtualKey != 0 && virtualKey == _settings.OcrShortcutVk;
        }

        // Helpers
        private bool EnsureDifferentTarget(string effectiveSource, out string newTarget)
        {
            newTarget = (CmbTarget.SelectedItem as LtLanguage)?.code ?? "en";
            if (!effectiveSource.Equals(newTarget, StringComparison.OrdinalIgnoreCase))
                return true;

            if (TryGetDifferentTargetCode(effectiveSource, newTarget, out var replacement) && TrySetTarget(replacement))
            {
                newTarget = replacement;
                SetStatus($"Target auto-changed to {replacement.ToUpperInvariant()}");
                return true;
            }

            return false;
        }

        internal bool EnsureDifferentTargetForFloating(string effectiveSource, string currentTarget, out string newTarget)
        {
            newTarget = currentTarget;
            if (!effectiveSource.Equals(currentTarget, StringComparison.OrdinalIgnoreCase))
                return true;

            if (TryGetDifferentTargetCode(effectiveSource, currentTarget, out var replacement))
            {
                newTarget = replacement;
                SetCurrentTargetCode(replacement);
                SetStatus($"Target auto-changed to {replacement.ToUpperInvariant()}");
                return true;
            }

            return false;
        }

        private bool TryGetDifferentTargetCode(string effectiveSource, string currentTarget, out string newTarget)
        {
            return TargetLanguageHelper.TryGetDifferentTargetCode(_languages, effectiveSource, currentTarget, out newTarget);
        }

        private bool TrySetTarget(string code)
        {
            int idx = FindIndexByCode(CmbTarget.Items, code);
            if (idx >= 0) { CmbTarget.SelectedIndex = idx; return true; }
            return false;
        }

        private static int FindIndexByCode(System.Collections.IList items, string code)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] is LtLanguage l && l.code.Equals(code, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private void NativeHookService_MouseLeftButtonReleased()
        {
            Dispatcher.InvokeAsync(async () => await ShowSelectionLauncherAsync());
        }

        private void NativeHookService_KeyPressed(int vkCode, bool ctrlDown, bool shiftDown, bool altDown, bool winDown)
        {
            bool modifiersMatch = (!_settings.CtrlRequired || ctrlDown)
                && (!_settings.ShiftRequired || shiftDown)
                && (!_settings.AltRequired || altDown)
                && (!_settings.WinRequired || winDown);

            if (!modifiersMatch || vkCode != _settings.HotkeyVk)
                return;

            if (_settings.HotkeyPressCount <= 1)
            {
                Dispatcher.Invoke(() => SetStatus($"{_settings.GetDisplayString()} detected: capturing selection..."));
                Dispatcher.InvokeAsync(async () => await CopySelectionAndTranslateAsync());
                _lastHotkey = DateTime.MinValue;
                return;
            }

            var now = DateTime.UtcNow;
            if (now - _lastHotkey <= TimeSpan.FromMilliseconds(_settings.ThresholdMs))
            {
                Dispatcher.Invoke(() => SetStatus($"Double {_settings.GetDisplayString()} detected: capturing selection..."));
                Dispatcher.InvokeAsync(async () => await CopySelectionAndTranslateAsync());
                _lastHotkey = DateTime.MinValue;
                return;
            }

            _lastHotkey = now;
        }

        private void ShowRetryMessage(string title, string message, Func<Task> retryAction)
        {
            var result = global::System.Windows.MessageBox.Show(
                this,
                $"{message}\n\nRetry?",
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
                _ = Dispatcher.InvokeAsync(async () => await retryAction());
        }

        private bool IsWriteTabActive() => TabsMain?.SelectedIndex == 1;

        private void SetStatus(string message) => TxtStatus.Text = message;

    }

    // Models
    internal class TranslateRequest
    {
        public string q { get; set; } = "";
        public string source { get; set; } = "auto";
        public string target { get; set; } = "en";
        public string format { get; set; } = "text";
        public string api_key { get; set; } = "";
    }

    internal class TranslateResponse
    {
        public string translatedText { get; set; } = "";
    }

    internal class DetectCandidate
    {
        public string language { get; set; } = "";
        public double confidence { get; set; }
    }

    internal class LanguageToolResponse
    {
        public List<LanguageToolMatch> matches { get; set; } = new();
    }

    internal class LanguageToolWriteResult
    {
        public string CorrectedText { get; set; } = "";
        public List<LanguageToolSegment> Segments { get; set; } = new();
    }

    internal class LanguageToolSegment
    {
        public string Text { get; set; } = "";
        public bool IsChanged { get; set; }
    }

    internal class LanguageToolMatch
    {
        public int offset { get; set; }
        public int length { get; set; }
        public List<LanguageToolReplacement> replacements { get; set; } = new();
    }

    internal class LanguageToolReplacement
    {
        public string value { get; set; } = "";
    }

    public class LtLanguage
    {
        public string code { get; set; } = "";
        public string name { get; set; } = "";
        public string display => $"{name} ({code})";
        public LtLanguage() { }
        public LtLanguage(string code, string name) { this.code = code; this.name = name; }
    }

}