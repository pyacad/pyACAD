using System;
using System.Runtime.CompilerServices;
using System.Windows;
using Autodesk.AutoCAD.Windows;

namespace PyAcad.Runtime.Panes
{
    /// <summary>
    /// One AutoCAD PaletteSet around a bundle's pane.xaml. The only file that touches
    /// the palette API (AcMgd), kept out of the registry so the test host never loads it.
    /// </summary>
    internal sealed class AcadPalette : IPaneHandle
    {
        private readonly PaletteSet _set;

        public event Action VisibleChanged;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IPaneHandle Create(string title, Guid id, FrameworkElement content, string tooltip)
        {
            var set = new PaletteSet(title, id)
            {
                Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton |
                        PaletteSetStyles.Snappable | PaletteSetStyles.ShowPropertiesMenu,
                DockEnabled = DockSides.Left | DockSides.Right,
                MinimumSize = new System.Drawing.Size(220, 160),
                // Text boxes in the pane must get keystrokes, not the command line.
                KeepFocus = true,
            };
            set.AddVisual(title, content);
            return new AcadPalette(set);
        }

        private AcadPalette(PaletteSet set)
        {
            _set = set;
            _set.StateChanged += (s, e) => VisibleChanged?.Invoke();
        }

        public bool Visible
        {
            get => !_set.IsDisposed && _set.Visible;
            set
            {
                if (_set.IsDisposed) return;
                var before = _set.Visible;
                _set.Visible = value;
                // PaletteSet.StateChanged does not fire for a programmatic Visible change
                // (pyacad.panes.show, a dock pane script hiding itself), so the ribbon
                // toggle would stay stale; the handler is idempotent, so a second raise
                // from StateChanged on a user action is harmless.
                if (before != _set.Visible) VisibleChanged?.Invoke();
            }
        }

        public void Dispose()
        {
            try
            {
                if (_set.IsDisposed) return;
                _set.Visible = false;
                _set.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Could not close a palette", ex);
            }
        }
    }
}
