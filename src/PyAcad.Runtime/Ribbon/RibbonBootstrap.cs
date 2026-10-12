using System;
using System.Runtime.CompilerServices;
using Autodesk.Windows;

namespace PyAcad.Runtime.Ribbon
{
    /// <summary>
    /// Ribbon-availability timing for early (Loader.Initialize) startup, and the
    /// rebuild after AutoCAD throws the ribbon away: switching workspace or loading a
    /// CUIx recreates every tab from the customization file, and add-in tabs are not
    /// in it. The ribbon palette set says when a workspace finished loading.
    /// </summary>
    public static class RibbonBootstrap
    {
        private static bool _workspaceHooked;

        /// <summary>
        /// Runs <paramref name="build"/> as soon as the AdWindows ribbon exists.
        /// At Loader.Initialize time the ribbon may not be constructed yet, so
        /// fall back to waiting on ComponentManager.ItemInitialized.
        /// </summary>
        public static void WhenRibbonReady(Action build)
        {
            if (ComponentManager.Ribbon != null)
            {
                Log.Info("Ribbon already available - building immediately.");
                build();
                return;
            }

            Log.Info("Ribbon not yet available - waiting for ComponentManager.ItemInitialized.");
            EventHandler<RibbonItemEventArgs> handler = null;
            handler = (sender, e) =>
            {
                if (ComponentManager.Ribbon == null) return;
                ComponentManager.ItemInitialized -= handler;
                Log.Info("Ribbon became available via ItemInitialized - building.");
                build();
            };
            ComponentManager.ItemInitialized += handler;
        }

        /// <summary>
        /// Calls <paramref name="rebuild"/> whenever a workspace finishes loading and the
        /// pyACAD tabs are gone. Safe to call more than once; the hook is taken once.
        /// </summary>
        public static void RebuildOnWorkspaceLoad(Func<bool> tabsPresent, Action rebuild)
        {
            if (_workspaceHooked) return;
            try
            {
                HookWorkspaceLoaded(() =>
                {
                    if (tabsPresent()) return;
                    Log.Info("Workspace loaded without the pyACAD tabs - rebuilding the ribbon.");
                    try { rebuild(); }
                    catch (Exception ex) { Log.Error("Ribbon rebuild after workspace load failed", ex); }
                });
                _workspaceHooked = true;
            }
            catch (Exception ex)
            {
                Log.Info("Workspace-load rebuild not hooked: " + ex.GetType().Name);
            }
        }

        // AcWindows: RibbonServices owns the palette set that hosts the ribbon and raises
        // WorkspaceLoaded after each workspace or CUIx load. The palette set may not exist
        // at Initialize time, so wait for its creation event when needed.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HookWorkspaceLoaded(Action onLoaded)
        {
            var set = Autodesk.AutoCAD.Ribbon.RibbonServices.RibbonPaletteSet;
            if (set != null)
            {
                set.WorkspaceLoaded += (s, e) => onLoaded();
                return;
            }
            EventHandler created = null;
            created = (s, e) =>
            {
                var late = Autodesk.AutoCAD.Ribbon.RibbonServices.RibbonPaletteSet;
                if (late == null) return;
                Autodesk.AutoCAD.Ribbon.RibbonServices.RibbonPaletteSetCreated -= created;
                late.WorkspaceLoaded += (s2, e2) => onLoaded();
            };
            Autodesk.AutoCAD.Ribbon.RibbonServices.RibbonPaletteSetCreated += created;
        }
    }
}
