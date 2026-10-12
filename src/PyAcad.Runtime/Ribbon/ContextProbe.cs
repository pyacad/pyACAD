using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace PyAcad.Runtime.Ribbon
{
    /// <summary>
    /// One snapshot of the drawing state the context conditions ask about, read in a
    /// single pass so N gated buttons cost one trip into the database. Every read is
    /// NoInlining and the caller catches, so the test host (no AutoCAD) gets "nothing".
    /// </summary>
    internal sealed class ContextProbe
    {
        public bool HasDocument;
        public bool HasSelection;
        public bool ModelSpace;
        public bool PaperSpace;
        public bool HasXrefs;
        public bool HasBlocks;
        public bool HasLayouts;
        public bool MultiDocument;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ContextProbe Read()
        {
            var probe = new ContextProbe();
            var docs = Application.DocumentManager;
            var doc = docs.MdiActiveDocument;
            probe.MultiDocument = docs.Count > 1;
            if (doc == null) return probe;
            probe.HasDocument = true;

            var db = doc.TryGetDatabase();
            if (db == null) return probe;
            probe.ModelSpace = db.TileMode;
            probe.PaperSpace = !db.TileMode;

            try
            {
                var implied = doc.Editor.SelectImplied();
                probe.HasSelection = implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Mid-command the editor refuses; a button greys for a moment, nothing more.
            }

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                probe.HasLayouts = layouts.Count > 1;   // Model always counts for one

                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (var id in table)
                {
                    var record = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (record.IsLayout || record.IsAnonymous || record.IsDependent) continue;
                    if (record.IsFromExternalReference || record.IsFromOverlayReference) probe.HasXrefs = true;
                    else probe.HasBlocks = true;
                    if (probe.HasXrefs && probe.HasBlocks) break;
                }
            }
            return probe;
        }
    }
}
