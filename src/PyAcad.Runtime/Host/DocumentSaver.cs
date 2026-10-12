using System;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace PyAcad.Runtime.Host
{
    /// <summary>
    /// Writes the active drawing back to its own file for <c>pyacad run --save</c>. A
    /// Core Console session has no Save dialog, so the database is saved directly in the
    /// format it was opened with.
    /// </summary>
    public static class DocumentSaver
    {
        /// <summary>Null when saved; otherwise why not (no drawing, wrong drawing, host error).</summary>
        public static string SaveActive(string expectedPath)
        {
            try
            {
                return SaveCore(expectedPath);
            }
            catch (Exception ex)
            {
                Log.Error("Save failed", ex);
                return ex.Message;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string SaveCore(string expectedPath)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return "No drawing is open.";
            if (!doc.IsNamedDrawing) return "The drawing has never been saved; there is no path to save to.";
            var db = doc.Database;
            var target = db.Filename;
            if (!string.IsNullOrEmpty(expectedPath) && !SamePath(target, expectedPath))
                return $"The open drawing is '{target}', not '{expectedPath}'.";
            if (doc.IsReadOnly) return "The drawing is read-only.";
            using (doc.LockDocument())
                db.SaveAs(target, true, DwgVersion.Current, db.SecurityParameters);
            return null;
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }
    }
}
