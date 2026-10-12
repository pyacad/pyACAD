using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Markup;

namespace PyAcad.Runtime.Panes
{
    /// <summary>The host-free half of dock panes: identity and XAML loading.</summary>
    public static class PaneContent
    {
        /// <summary>
        /// A palette's GUID is how AutoCAD remembers its size, dock side and visibility
        /// between sessions, so it must be the same every start: derived from the bundle
        /// key, which survives renumbering and reinstalls.
        /// </summary>
        public static Guid IdFor(string bundleKey)
        {
            var key = (bundleKey ?? "").Trim().ToLowerInvariant().Replace('\\', '/');
            using (var md5 = MD5.Create())
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("pyacad.dockpane:" + key)));
        }

        /// <summary>
        /// pane.xaml as the element the palette hosts. A Window root is unwrapped (its
        /// content is what matters; a palette cannot host a Window), anything else is
        /// used as is.
        /// </summary>
        public static FrameworkElement Load(string xamlPath)
        {
            object root;
            using (var stream = File.OpenRead(xamlPath))
                root = XamlReader.Load(stream);
            if (root is Window window)
            {
                var content = window.Content as FrameworkElement;
                window.Content = null;
                return content ?? new System.Windows.Controls.Grid();
            }
            return root as FrameworkElement
                   ?? throw new InvalidOperationException(xamlPath + " does not describe a WPF element.");
        }
    }
}
