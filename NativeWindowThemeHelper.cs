using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace KemTranslate
{
    internal static class NativeWindowThemeHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

        public static void ApplyApplicationTheme(bool dark)
        {
            var app = Application.Current;
            if (app == null)
                return;

            for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var dictionary = app.Resources.MergedDictionaries[i];
                if (IsThemeDictionary(dictionary))
                    app.Resources.MergedDictionaries.RemoveAt(i);
            }

            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(dark ? "/Themes/Dark.xaml" : "/Themes/Light.xaml", UriKind.Relative)
            });
        }

        public static void ApplyWindowBrushes(Window window)
        {
            var app = Application.Current;
            if (window == null || app == null)
                return;

            if (app.Resources["WindowBackground"] is Brush background)
                window.Background = background;

            if (app.Resources["ForegroundBrush"] is Brush foreground)
                window.Foreground = foreground;
        }

        public static void ApplyWindowTheme(Window window, bool dark)
        {
            ApplyApplicationTheme(dark);
            ApplyWindowBrushes(window);
            Apply(window, dark);
        }

        public static void Apply(Window window, bool dark)
        {
            if (window == null)
                return;

            void ApplyCore()
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                    return;

                int enabled = dark ? 1 : 0;
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref enabled, sizeof(int));

                SetWindowTheme(handle, dark ? "DarkMode_Explorer" : "Explorer", null);

                int captionColor = dark ? 0x000000 : 0xFFFFFF;
                int textColor = dark ? 0xF0F0F0 : 0x000000;
                int borderColor = dark ? 0x000000 : 0xD8D8D8;

                DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
                DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
                DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            }

            if (window.IsLoaded)
            {
                ApplyCore();
                return;
            }

            void OnSourceInitialized(object? sender, EventArgs e)
            {
                window.SourceInitialized -= OnSourceInitialized;
                ApplyCore();
            }

            window.SourceInitialized += OnSourceInitialized;
        }

        private static bool IsThemeDictionary(ResourceDictionary dictionary)
        {
            var source = dictionary.Source?.OriginalString;
            return source != null
                && (source.EndsWith("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase)
                    || source.EndsWith("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase));
        }
    }
}
