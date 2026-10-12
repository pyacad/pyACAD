using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace PyAcad.Runtime.Host
{
    /// <summary>
    /// The pickfirst selection, read and written from application context (output
    /// window links, the AI panel), so each call takes the document lock itself.
    /// </summary>
    public static class Selections
    {
        /// <summary>
        /// Selects one object in the active drawing. The item is what a script handed to
        /// output.element_link: an ObjectId, a Handle, a handle string ("2F3A") or a long.
        /// </summary>
        public static void Select(object item)
        {
            DocumentLocks.Locked(() =>
            {
                try { SelectCore(item); }
                catch (Exception ex) { Log.Error("Could not select the linked object", ex); }
            });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SelectCore(object item)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var id = Resolve(doc.Database, item);
            if (id.IsNull || id.IsErased) return;
            doc.Editor.SetImpliedSelection(new[] { id });
        }

        private static ObjectId Resolve(Database db, object item)
        {
            switch (item)
            {
                case ObjectId id: return id;
                case Handle handle: return FromHandle(db, handle);
                case long raw: return FromHandle(db, new Handle(raw));
                case int raw: return FromHandle(db, new Handle(raw));
                case string text:
                    return long.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var parsed)
                        ? FromHandle(db, new Handle(parsed))
                        : ObjectId.Null;
                default:
                    return ObjectId.Null;
            }
        }

        private static ObjectId FromHandle(Database db, Handle handle) =>
            db.TryGetObjectId(handle, out var id) ? id : ObjectId.Null;

        /// <summary>
        /// For the AI panel: how many objects are selected, and per DXF type the .NET
        /// property names of its wrapper (never values, so nothing from the drawing leaves
        /// the machine). Reads at most <paramref name="maxItems"/> objects.
        /// </summary>
        public static (int count, Dictionary<string, List<string>> properties) ReadTypesAndPropertyNames(int maxItems)
        {
            var properties = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var count = 0;
            DocumentLocks.Locked(() =>
            {
                try { count = ReadCore(maxItems, properties); }
                catch (Exception ex) { Log.Error("Could not read the selection", ex); }
            });
            return (count, properties);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadCore(int maxItems, Dictionary<string, List<string>> properties)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            var implied = doc.Editor.SelectImplied();
            if (implied.Status != PromptStatus.OK || implied.Value == null) return 0;
            var ids = implied.Value.GetObjectIds();

            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var read = 0;
                foreach (var id in ids)
                {
                    if (read++ >= maxItems) break;
                    var obj = tr.GetObject(id, OpenMode.ForRead);
                    var type = obj.GetRXClass().DxfName ?? obj.GetType().Name;
                    if (properties.ContainsKey(type)) continue;
                    var names = new List<string>();
                    foreach (var prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        if (prop.CanRead && prop.GetIndexParameters().Length == 0) names.Add(prop.Name);
                    names.Sort(StringComparer.Ordinal);
                    properties[type] = names;
                }
            }
            return ids.Length;
        }
    }
}
