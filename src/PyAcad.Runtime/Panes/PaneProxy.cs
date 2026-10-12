using System.Windows;

namespace PyAcad.Runtime.Panes
{
    /// <summary>
    /// What a *.dockpane script.py sees as <c>__pane__</c>: the loaded XAML root plus the
    /// handle the ribbon toggle uses, in the shape docs/authoring/buttons.html promises
    /// (Find, Content, Title, BundleKey, Visible).
    /// </summary>
    public sealed class PaneProxy
    {
        private readonly IPaneHandle _handle;

        public PaneProxy(string title, string bundleKey, FrameworkElement content, IPaneHandle handle)
        {
            Title = title;
            BundleKey = bundleKey;
            Content = content;
            _handle = handle;
        }

        /// <summary>The panel's resolved title.</summary>
        public string Title { get; }

        /// <summary>The panel's own bundle key, for pyacad.panes calls on itself.</summary>
        public string BundleKey { get; }

        /// <summary>The root element pane.xaml produced.</summary>
        public FrameworkElement Content { get; }

        /// <summary>Shows or hides the palette.</summary>
        public bool Visible
        {
            get => _handle != null && _handle.Visible;
            set { if (_handle != null) _handle.Visible = value; }
        }

        /// <summary>The named element from pane.xaml, or null.</summary>
        public object Find(string name) => name == null ? null : Content?.FindName(name);
    }
}
