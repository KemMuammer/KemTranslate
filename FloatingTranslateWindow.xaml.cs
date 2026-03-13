using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KemTranslate
{
    public partial class FloatingTranslateWindow : Window
    {
        private enum FloatingOperationMode
        {
            Translate,
            Write
        }

        private const double CollapsedCompactWidth = 50;
        private const double CollapsedExpandedWidth = 97;
        private const double CollapsedHeight = 50;
        private static readonly Duration CollapsedSlideDuration = new(TimeSpan.FromMilliseconds(160));

        private readonly MainWindow _mainWindow;
        private bool _allowClose;
        private bool _updatingTarget;
        private FloatingOperationMode _currentMode = FloatingOperationMode.Translate;
        private Point? _anchorScreenPoint;
        private string _pendingSourceText = string.Empty;
        private string _currentSourceText = string.Empty;
        private string _currentTranslatedText = string.Empty;

        public FloatingTranslateWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            UpdateCollapsedHoverState(false, false);
            UpdateModeUi();
        }

        public void PrepareForExit()
        {
            _allowClose = true;
        }

        internal void ApplyEditorFontSettings(FontFamily fontFamily, double fontSize)
        {
            TxtTranslation.FontFamily = fontFamily;
            TxtTranslation.FontSize = fontSize;
        }

        internal void RefreshLanguages()
        {
            EnsureLanguages();
        }

        public bool IsCollapsedMode => IsVisible && CollapsedRoot.Visibility == Visibility.Visible;
        public bool IsExpandedMode => IsVisible && ExpandedRoot.Visibility == Visibility.Visible;

        public bool ContainsScreenPoint(Point screenPoint)
        {
            if (!IsVisible)
                return false;

            return screenPoint.X >= Left
                && screenPoint.X <= Left + ActualWidth
                && screenPoint.Y >= Top
                && screenPoint.Y <= Top + ActualHeight;
        }

        public void HideFloating()
        {
            Hide();
        }

        private void DismissFloating()
        {
            _pendingSourceText = string.Empty;
            _currentSourceText = string.Empty;
            _currentTranslatedText = string.Empty;
            Hide();
        }

        public void ShowCollapsed()
        {
            SetMode(expanded: false);
            ShowFloating(false);
        }

        public void ShowLauncher(string sourceText, Point screenPoint)
        {
            _pendingSourceText = sourceText.Trim();
            _anchorScreenPoint = screenPoint;
            SetMode(expanded: false);
            PositionNear(screenPoint);
            ShowFloating(false);
        }

        public async Task ShowCurrentModeAsync(string sourceText, bool activateWindow = true, Point? screenPoint = null)
        {
            if (_currentMode == FloatingOperationMode.Write)
                await ShowWriteAsync(sourceText, activateWindow, screenPoint);
            else
                await ShowTranslationAsync(sourceText, activateWindow, screenPoint);
        }

        public async Task ShowTranslationAsync(string sourceText, bool activateWindow = true, Point? screenPoint = null)
        {
            _currentMode = FloatingOperationMode.Translate;
            UpdateModeUi();
            ApplyEditorFontSettings(_mainWindow.GetEditorFontFamily(), _mainWindow.GetEditorFontSize());
            _currentSourceText = sourceText.Trim();
            _pendingSourceText = _currentSourceText;
            EnsureLanguages();
            if (!IsExpandedMode)
                SetMode(expanded: true);
            PositionUsingAnchor(screenPoint);
            TxtSourceInfo.Text = "Detecting language...";
            RenderPlainOutput("Translating...");
            ShowFloating(activateWindow);

            if (CmbTarget.SelectedItem is LtLanguage target)
            {
                var result = await _mainWindow.TranslateForFloatingAsync(_currentSourceText, target.code);
                if (!target.code.Equals(result.effectiveTarget, StringComparison.OrdinalIgnoreCase))
                {
                    _updatingTarget = true;
                    CmbTarget.SelectedItem = ((System.Collections.Generic.IEnumerable<LtLanguage>)CmbTarget.ItemsSource)
                        .FirstOrDefault(x => x.code.Equals(result.effectiveTarget, StringComparison.OrdinalIgnoreCase));
                    _updatingTarget = false;
                }

                _currentTranslatedText = result.translatedText;
                RenderPlainOutput(result.translatedText);
                TxtSourceInfo.Text = $"{_mainWindow.GetLanguageDisplayName(result.effectiveSource)} → {_mainWindow.GetLanguageDisplayName(result.effectiveTarget)}";
            }
        }

        public async Task ShowWriteAsync(string sourceText, bool activateWindow = true, Point? screenPoint = null)
        {
            _currentMode = FloatingOperationMode.Write;
            UpdateModeUi();
            ApplyEditorFontSettings(_mainWindow.GetEditorFontFamily(), _mainWindow.GetEditorFontSize());
            _currentSourceText = sourceText.Trim();
            _pendingSourceText = _currentSourceText;
            if (!IsExpandedMode)
                SetMode(expanded: true);
            PositionUsingAnchor(screenPoint);

            TxtSourceInfo.Text = "Fixing text...";
            RenderPlainOutput("Improving...");
            ShowFloating(activateWindow);

            var result = await _mainWindow.ImproveWritingDetailedForFloatingAsync(_currentSourceText);
            _currentTranslatedText = result.CorrectedText;
            RenderWriteOutput(result);
            TxtSourceInfo.Text = result.CorrectedText == _currentSourceText ? "No suggestions found." : "Text improved.";
        }

        private void UpdateModeUi()
        {
            ApplyModeButtonStyle(BtnModeTranslate, _currentMode == FloatingOperationMode.Translate);
            ApplyModeButtonStyle(BtnModeWrite, _currentMode == FloatingOperationMode.Write);
            CmbTarget.Visibility = _currentMode == FloatingOperationMode.Translate ? Visibility.Visible : Visibility.Collapsed;
            TxtWriteModeInfo.Visibility = _currentMode == FloatingOperationMode.Write ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyModeButtonStyle(Button button, bool isActive)
        {
            button.Background = (Brush)FindResource(isActive ? "AccentBrush" : "ButtonBackground");
            button.BorderBrush = (Brush)FindResource(isActive ? "AccentBrush" : "FloatingBorderBrush");
            button.Foreground = (Brush)FindResource(isActive ? "AccentForegroundBrush" : "ForegroundBrush");
        }

        private async Task<string?> GetRequestedSourceTextAsync()
        {
            var localSelectedText = new TextRange(TxtTranslation.Selection.Start, TxtTranslation.Selection.End).Text?.Trim();
            if (!string.IsNullOrWhiteSpace(localSelectedText))
                return localSelectedText;

            if (!string.IsNullOrWhiteSpace(_currentSourceText))
                return _currentSourceText;

            var selectedText = await _mainWindow.CaptureSelectionFromLastWindowAsync();
            return string.IsNullOrWhiteSpace(selectedText) ? null : selectedText;
        }

        private void RenderPlainOutput(string text)
        {
            TxtTranslation.Document = RichTextDocumentHelper.CreateDocument(
                TxtTranslation,
                new Paragraph(new Run(text)) { Margin = new Thickness(0) });
        }

        private void RenderWriteOutput(LanguageToolWriteResult result)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            foreach (var segment in result.Segments)
            {
                var run = new Run(segment.Text);
                if (segment.IsChanged)
                {
                    run.TextDecorations = TextDecorations.Underline;
                    run.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                }

                paragraph.Inlines.Add(run);
            }

            TxtTranslation.Document = RichTextDocumentHelper.CreateDocument(TxtTranslation, paragraph);
        }

        private void EnsureLanguages()
        {
            var languages = _mainWindow.GetTranslationLanguages().ToList();
            if (languages.Count == 0)
                return;

            var selectedCode = (CmbTarget.SelectedItem as LtLanguage)?.code;
            var preferredCode = string.IsNullOrWhiteSpace(selectedCode) ? _mainWindow.GetCurrentTargetCode() : selectedCode;

            _updatingTarget = true;
            CmbTarget.ItemsSource = languages;
            CmbTarget.SelectedItem = languages.FirstOrDefault(x => x.code.Equals(preferredCode, StringComparison.OrdinalIgnoreCase)) ?? languages.First();
            _updatingTarget = false;
        }

        private static Rect GetWorkAreaForPoint(Point screenPoint)
        {
            var screen = global::System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y)));
            var area = screen.WorkingArea;
            return new Rect(area.Left, area.Top, area.Width, area.Height);
        }

        private Rect GetCurrentWorkArea()
        {
            double referenceX = Left;
            double referenceY = Top;

            if (double.IsNaN(referenceX) || double.IsNaN(referenceY))
            {
                referenceX = SystemParameters.WorkArea.Left;
                referenceY = SystemParameters.WorkArea.Top;
            }

            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;
            if (width <= 0)
                width = CollapsedExpandedWidth;
            if (height <= 0)
                height = CollapsedHeight;

            return GetWorkAreaForPoint(new Point(referenceX + (width / 2), referenceY + (height / 2)));
        }

        private void SetMode(bool expanded)
        {
            var workArea = GetCurrentWorkArea();
            double right = Left + (ActualWidth > 0 ? ActualWidth : Width);
            double bottom = Top + (ActualHeight > 0 ? ActualHeight : Height);

            if (double.IsNaN(right) || right <= 0 || double.IsNaN(bottom) || bottom <= 0)
            {
                right = workArea.Right - 16;
                bottom = workArea.Bottom - 16;
            }

            CollapsedRoot.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            ExpandedRoot.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

            if (expanded)
            {
                BeginAnimation(WidthProperty, null);
                CollapsedRoot.BeginAnimation(WidthProperty, null);
                BtnCollapsedHide.BeginAnimation(OpacityProperty, null);
                Width = 480;
                Height = 300;
            }
            else
            {
                UpdateCollapsedHoverState(false, false);
                Width = CollapsedCompactWidth;
                Height = CollapsedHeight;
            }

            Left = Math.Max(workArea.Left + 8, Math.Min(workArea.Right - Width - 8, right - Width));
            Top = Math.Max(workArea.Top + 8, Math.Min(workArea.Bottom - Height - 8, bottom - Height));
        }

        private void UpdateCollapsedHoverState(bool isHovering, bool preserveRightEdge = false)
        {
            double targetWidth = isHovering ? CollapsedExpandedWidth : CollapsedCompactWidth;

            BtnCollapsedHide.IsHitTestVisible = isHovering;
            BtnCollapsedHide.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = isHovering ? 1 : 0,
                Duration = CollapsedSlideDuration,
                FillBehavior = FillBehavior.HoldEnd
            });

            if (BtnCollapsedHide.RenderTransform is TranslateTransform translateTransform)
            {
                translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
                {
                    To = isHovering ? 0 : -18,
                    Duration = CollapsedSlideDuration,
                    FillBehavior = FillBehavior.HoldEnd
                });
            }

            CollapsedRoot.BeginAnimation(WidthProperty, null);
            BeginAnimation(WidthProperty, null);
            CollapsedRoot.Width = targetWidth;
            Width = targetWidth;
            Height = CollapsedHeight;
        }

        private void PositionNear(Point screenPoint)
        {
            var workArea = GetWorkAreaForPoint(screenPoint);
            Left = Math.Min(workArea.Right - Width - 8, Math.Max(workArea.Left + 8, screenPoint.X + 12));
            Top = Math.Min(workArea.Bottom - Height - 8, Math.Max(workArea.Top + 8, screenPoint.Y + 12));
        }

        private void PositionUsingAnchor(Point? screenPoint)
        {
            if (screenPoint.HasValue)
            {
                _anchorScreenPoint = screenPoint.Value;
                PositionNear(screenPoint.Value);
                return;
            }

            if (_anchorScreenPoint.HasValue)
                PositionNear(_anchorScreenPoint.Value);
        }

        private void ShowFloating(bool activate)
        {
            if (!IsVisible)
                Show();

            Topmost = true;
            if (activate)
            {
                Activate();
                Focus();
            }
        }

        private async void BtnCollapsedOpen_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_pendingSourceText))
            {
                await ShowCurrentModeAsync(_pendingSourceText, true, _anchorScreenPoint);
                return;
            }

            SetMode(expanded: true);
            PositionUsingAnchor(_anchorScreenPoint);
            EnsureLanguages();
            UpdateModeUi();
            ShowFloating(true);
        }

        private void BtnCollapsedHide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void CollapsedRoot_MouseEnter(object sender, MouseEventArgs e)
        {
            if (CollapsedRoot.Visibility == Visibility.Visible)
                UpdateCollapsedHoverState(true);
        }

        private void CollapsedRoot_MouseLeave(object sender, MouseEventArgs e)
        {
            if (CollapsedRoot.Visibility == Visibility.Visible)
                UpdateCollapsedHoverState(false);
        }

        private void BtnCollapse_Click(object sender, RoutedEventArgs e)
        {
            DismissFloating();
        }

        private void DragSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            if (e.OriginalSource is not DependencyObject dependencyObject || IsInteractiveElement(dependencyObject))
                return;

            try
            {
                Activate();
                DragMove();
                e.Handled = true;
            }
            catch { }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.OpenSettingsDialog();
            Topmost = true;
        }

        private async void BtnTranslateSelection_Click(object sender, RoutedEventArgs e)
        {
            var selectedText = await GetRequestedSourceTextAsync();
            if (!string.IsNullOrWhiteSpace(selectedText))
                await ShowTranslationAsync(selectedText, true);
        }

        private async void BtnWriteSelection_Click(object sender, RoutedEventArgs e)
        {
            var selectedText = await GetRequestedSourceTextAsync();
            if (!string.IsNullOrWhiteSpace(selectedText))
                await ShowWriteAsync(selectedText, true);
        }

        private async void BtnOcrCapture_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            var capturedText = await _mainWindow.CaptureTextFromScreenRegionAsync(false);
            if (string.IsNullOrWhiteSpace(capturedText))
            {
                ShowFloating(true);
                return;
            }

            if (_currentMode == FloatingOperationMode.Write)
                await ShowWriteAsync(capturedText, true, _anchorScreenPoint);
            else
                await ShowTranslationAsync(capturedText, true, _anchorScreenPoint);
        }

        private async void CmbTarget_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_currentMode != FloatingOperationMode.Translate)
                return;

            if (_updatingTarget || string.IsNullOrWhiteSpace(_currentSourceText) || CmbTarget.SelectedItem is not LtLanguage target)
                return;

            _mainWindow.SetCurrentTargetCode(target.code);
            await ShowTranslationAsync(_currentSourceText, true);
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_currentTranslatedText))
                global::System.Windows.Clipboard.SetText(_currentTranslatedText);
        }

        private void BtnOpenMain_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == FloatingOperationMode.Write)
            {
                _mainWindow.OpenMainWindowWithWrite(_currentSourceText, _currentTranslatedText);
            }
            else if (CmbTarget.SelectedItem is LtLanguage target)
            {
                _mainWindow.OpenMainWindowWithTranslation(_currentSourceText, _currentTranslatedText, target.code);
            }
        }

        private async void BtnReplace_Click(object sender, RoutedEventArgs e)
        {
            if (await _mainWindow.ReplaceLastCapturedTextAsync(_currentTranslatedText))
                DismissFloating();
        }

        private static bool IsInteractiveElement(DependencyObject dependencyObject)
        {
            return FindParent<System.Windows.Controls.Button>(dependencyObject) != null
                || FindParent<System.Windows.Controls.Primitives.ToggleButton>(dependencyObject) != null
                || FindParent<System.Windows.Controls.TextBox>(dependencyObject) != null
                || FindParent<System.Windows.Controls.RichTextBox>(dependencyObject) != null
                || FindParent<System.Windows.Controls.ComboBox>(dependencyObject) != null
                || FindParent<System.Windows.Controls.ComboBoxItem>(dependencyObject) != null
                || FindParent<System.Windows.Controls.Primitives.ScrollBar>(dependencyObject) != null;
        }

        private static DependencyObject? GetParentObject(DependencyObject child)
        {
            return child switch
            {
                FrameworkElement frameworkElement => frameworkElement.Parent
                    ?? System.Windows.Media.VisualTreeHelper.GetParent(frameworkElement),
                FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
                ContentElement contentElement => ContentOperations.GetParent(contentElement),
                Visual or System.Windows.Media.Media3D.Visual3D => System.Windows.Media.VisualTreeHelper.GetParent(child),
                _ => LogicalTreeHelper.GetParent(child)
            };
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T match)
                    return match;

                current = GetParentObject(current);
            }

            return null;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnClosing(e);
        }
    }
}
