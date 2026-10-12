using System;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices.Core;

namespace PyAcad.Runtime.Host
{
    /// <summary>
    /// Runs work under the active drawing's document lock, for code that reaches the
    /// database from application context: hooks, palette callbacks, the LISP bridge and
    /// the direct-run fallback. A registered command never needs this, AutoCAD locks for it.
    /// Outside AutoCAD the work simply runs.
    /// </summary>
    public static class DocumentLocks
    {
        public static void Locked(Action work)
        {
            try
            {
                if (TryLocked(work)) return;
            }
            catch (Exception ex) when (IsHostMissing(ex))
            {
                // No AutoCAD here: fall through and run unlocked.
            }
            work();
        }

        /// <summary>False when there is no active document to lock.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryLocked(Action work)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            using (doc.LockDocument())
                work();
            return true;
        }

        private static bool IsHostMissing(Exception ex) =>
            ex is System.IO.FileNotFoundException || ex is TypeLoadException ||
            ex is System.IO.FileLoadException || ex is BadImageFormatException;
    }
}
