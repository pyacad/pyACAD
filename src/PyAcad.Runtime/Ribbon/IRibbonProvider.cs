using System.Collections.Generic;
using PyAcad.Runtime.Bundles;

namespace PyAcad.Runtime.Ribbon
{
    /// <summary>
    /// Ribbon backend seam. AdWindows injection is the primary implementation; the
    /// documented fallback (static generated plugin assembly, restart-based) would
    /// slot in here if AdWindows ever proves unstable on an AutoCAD release.
    /// </summary>
    public interface IRibbonProvider
    {
        /// <summary>Creates ribbon UI for the given extensions. Ribbon must exist.</summary>
        void Build(IReadOnlyList<ExtensionModel> extensions);

        /// <summary>Removes every ribbon element this provider created.</summary>
        void Teardown();

        /// <summary>True while every tab the last Build made is still on the ribbon.
        /// A workspace switch drops them without telling anyone.</summary>
        bool TabsPresent();
    }
}
