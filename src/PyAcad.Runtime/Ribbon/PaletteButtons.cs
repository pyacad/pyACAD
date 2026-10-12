using System;
using Autodesk.Windows;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Panes;

namespace PyAcad.Runtime.Ribbon
{
    /// <summary>
    /// The ribbon side of a dock pane: a toggle that shows or hides the palette and
    /// follows it when the user closes the palette from its own title bar.
    /// </summary>
    internal static class PaletteButtons
    {
        public static RibbonToggleButton Create(DockPaneModel model, IPaneHandle handle)
        {
            var button = new RibbonToggleButton
            {
                Id = "PYACAD_PANE_" + Math.Abs(model.Directory.ToLowerInvariant().GetHashCode()),
                Text = model.RibbonTitle,
                ShowText = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                IsChecked = handle.Visible,
            };
            if (!string.IsNullOrEmpty(model.Tooltip)) button.ToolTip = model.Tooltip;
            var dark = Output.PyAcadTheme.IsDark;
            AdWindowsRibbonProvider.ApplyIcons(button,
                dark ? model.DarkIconPath : model.IconPath,
                dark ? model.SmallDarkIconPath : model.SmallIconPath);

            button.CommandHandler = new RelayCommand(_ =>
            {
                try { handle.Visible = !handle.Visible; }
                catch (Exception ex) { Log.Error($"Could not toggle dock pane '{model.Title}'", ex); }
            });
            handle.VisibleChanged += () =>
            {
                var visible = handle.Visible;
                if (button.IsChecked != visible) button.IsChecked = visible;
            };
            return button;
        }
    }
}
