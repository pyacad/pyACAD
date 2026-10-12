using System;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices.Core;

namespace PyAcad.Runtime.Host
{
    /// <summary>
    /// The only reads of AutoCAD's application state. Every member is NoInlining so a
    /// caller can wrap it in try/catch and survive the type-load failure that happens
    /// where AcCoreMgd is absent (the test host). Core Console loads AcCoreMgd and AcDbMgd
    /// but not AcMgd, so nothing here may touch AcMgd or AcWindows types.
    /// </summary>
    internal static class AcadHostProbe
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IntPtr MainWindowHandle()
        {
            var window = Application.MainWindow;
            return window == null ? IntPtr.Zero : window.Handle;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool HasMainWindow() => Application.MainWindow != null;

        /// <summary>"25.1.x.y": AutoCAD's own release number, which HostGate maps to a year.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string VersionString() => Application.Version.ToString();

        /// <summary>COLORTHEME as AutoCAD reports it (0 dark, 1 light), or null where the
        /// variable does not exist (older releases, Core Console).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object ColorTheme()
        {
            try { return Application.GetSystemVariable("COLORTHEME"); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool HasActiveDocument() => Application.DocumentManager.MdiActiveDocument != null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int DocumentCount() => Application.DocumentManager.Count;

        /// <summary>The active drawing's full path, "untitled" for one never saved, null with none open.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ActiveDocumentPath()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return null;
            return doc.IsNamedDrawing ? doc.Name : "untitled";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool IsQuiescent() => Application.IsQuiescent;
    }
}
