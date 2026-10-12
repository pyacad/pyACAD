using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace PyAcad.Runtime.Forms
{
    /// <summary>Loads a bundle-shipped .xaml file into a Window (pyacad.forms.WPFWindow).</summary>
    public static class XamlHost
    {
        public static Window Load(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var root = XamlReader.Load(stream);
                if (root is Window window) return window;
                return new Window
                {
                    Content = root,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                };
            }
        }
    }
}
