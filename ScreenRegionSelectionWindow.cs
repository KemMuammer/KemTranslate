using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace KemTranslate
{
    internal sealed class ScreenRegionSelectionWindow : Window
    {
        private readonly Canvas _root;
        private readonly Rectangle _selectionRectangle;
        private Point _startPoint;
        private bool _isSelecting;

        public ScreenRegionSelectionWindow()
        {
            var virtualScreen = Forms.SystemInformation.VirtualScreen;

            Left = virtualScreen.Left;
            Top = virtualScreen.Top;
            Width = virtualScreen.Width;
            Height = virtualScreen.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
            ShowInTaskbar = false;
            Topmost = true;
            Cursor = Cursors.Cross;

            _root = new Canvas();
            _selectionRectangle = new Rectangle
            {
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Visibility = Visibility.Collapsed
            };

            _root.Children.Add(_selectionRectangle);
            Content = _root;

            PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            PreviewMouseMove += OnPreviewMouseMove;
            PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        public Rect SelectedBounds { get; private set; }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(this);
            _isSelecting = true;
            _selectionRectangle.Visibility = Visibility.Visible;
            UpdateSelection(_startPoint);
            CaptureMouse();
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting)
                return;

            UpdateSelection(e.GetPosition(this));
        }

        private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting)
                return;

            _isSelecting = false;
            ReleaseMouseCapture();
            UpdateSelection(e.GetPosition(this));

            if (SelectedBounds.Width < 2 || SelectedBounds.Height < 2)
            {
                DialogResult = false;
                Close();
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            DialogResult = false;
            Close();
        }

        private void UpdateSelection(Point currentPoint)
        {
            double x = Math.Min(_startPoint.X, currentPoint.X);
            double y = Math.Min(_startPoint.Y, currentPoint.Y);
            double width = Math.Abs(currentPoint.X - _startPoint.X);
            double height = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(_selectionRectangle, x);
            Canvas.SetTop(_selectionRectangle, y);
            _selectionRectangle.Width = width;
            _selectionRectangle.Height = height;

            SelectedBounds = new Rect(Left + x, Top + y, width, height);
        }
    }
}
