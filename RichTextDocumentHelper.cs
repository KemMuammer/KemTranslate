using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KemTranslate
{
    internal static class RichTextDocumentHelper
    {
        public static FlowDocument CreateDocument(RichTextBox richTextBox, Paragraph paragraph)
        {
            return new FlowDocument(paragraph)
            {
                PagePadding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = richTextBox.Foreground,
                FontFamily = richTextBox.FontFamily,
                FontSize = richTextBox.FontSize
            };
        }
    }
}
