using System;
using System.Collections.Generic;
using System.Windows;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Engine;
using PyAcad.Runtime.Execution;

namespace PyAcad.Runtime.Panes
{
    /// <summary>A built pane as the ribbon sees it: visibility and a change signal.</summary>
    public interface IPaneHandle : IDisposable
    {
        bool Visible { get; set; }
        event Action VisibleChanged;
    }

    /// <summary>
    /// Builds a palette per *.dockpane bundle at boot and on Reload, runs the bundle's
    /// script.py once against the loaded XAML (as <c>__pane__</c>), and keeps the handles
    /// the ribbon toggles use. No slots, no claims: AutoCAD keeps as many palettes as
    /// there are bundles and remembers each by its GUID.
    /// </summary>
    public static class PaletteRegistry
    {
        private static readonly Dictionary<string, IPaneHandle> Handles =
            new Dictionary<string, IPaneHandle>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Replaceable for tests; null means an AutoCAD PaletteSet (resolved only
        /// when a pane is built, so the test host never loads the palette API).</summary>
        internal static Func<string, Guid, FrameworkElement, string, IPaneHandle> CreatePalette { get; set; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static IPaneHandle CreateAcadPalette(string title, Guid id, FrameworkElement content, string tooltip) =>
            AcadPalette.Create(title, id, content, tooltip);

        public static IPaneHandle Find(string bundleKey) =>
            bundleKey != null && Handles.TryGetValue(bundleKey, out var handle) ? handle : null;

        /// <summary>Builds every dock pane; one that fails costs only itself.</summary>
        public static void Configure(IReadOnlyList<ExtensionModel> extensions)
        {
            Teardown();
            var steps = new StepRunner();
            foreach (var ext in extensions)
                foreach (var tab in ext.Tabs)
                    foreach (var panel in tab.Panels)
                        foreach (var item in AllItems(panel))
                            if (item is DockPaneModel pane)
                                steps.Run("Dock pane '" + pane.Title + "'", () => Build(pane));
            var problems = steps.Summary();
            if (problems != null)
            {
                Log.Error("Dock panes with problems: " + problems);
                try { Forms.Toast.Show("error", "Some dock panes could not be built", problems); }
                catch (Exception ex) { Log.Error("Could not show the dock pane toast", ex); }
            }
            if (Handles.Count > 0) Log.Info($"Dock panes built: {Handles.Count}.");
        }

        private static IEnumerable<PanelItem> AllItems(PanelModel panel)
        {
            foreach (var item in panel.Items) yield return item;
            foreach (var item in panel.Slideout) yield return item;
        }

        private static void Build(DockPaneModel pane)
        {
            var content = PaneContent.Load(pane.XamlPath);
            var create = CreatePalette ?? CreateAcadPalette;
            var handle = create(pane.Title, PaneContent.IdFor(pane.BundleKey), content, pane.Tooltip);
            Handles[pane.BundleKey] = handle;
            if (pane.ScriptPath != null) RunScript(pane, new PaneProxy(pane.Title, pane.BundleKey, content, handle));
        }

        /// <summary>script.py sees a PaneProxy (Find, Content, Title, BundleKey, Visible) as
        /// __pane__ and wires its own handlers.</summary>
        private static void RunScript(DockPaneModel pane, PaneProxy proxy)
        {
            var request = new ScriptRequest { ScriptPath = pane.ScriptPath };
            foreach (var path in pane.SearchPaths) request.SearchPaths.Add(path);
            request.Globals["__file__"] = pane.ScriptPath;
            request.Globals["__commandpath__"] = pane.Directory;
            request.Globals["__title__"] = pane.Title;
            request.Globals["__pane__"] = proxy;
            request.Globals["__pyacad__"] = PyAcadHost.Instance;
            request.Globals["__runner__"] = false;
            PyAcadHost.Instance.LogSource = pane.Title;
            try
            {
                var result = EngineManager.GetEngine(pane.EngineId).Execute(request);
                if (!result.Succeeded)
                    throw new InvalidOperationException("script.py failed:\n" + result.ErrorText);
            }
            finally
            {
                PyAcadHost.Instance.LogSource = null;
            }
        }

        public static void Teardown()
        {
            foreach (var handle in Handles.Values)
            {
                try { handle.Dispose(); }
                catch (Exception ex) { Log.Error("Could not dispose a dock pane", ex); }
            }
            Handles.Clear();
        }
    }
}
