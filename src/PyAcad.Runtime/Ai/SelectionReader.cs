using System;

namespace PyAcad.Runtime.Ai
{
    /// <summary>
    /// The API half of the attach-selection payload: reads the current selection's
    /// category and property NAMES and hands them to SelectionSummary. Values are
    /// never read, so nothing from the project can end up in the request. Needs a
    /// live AutoCAD; the pure half is what the tests cover.
    /// </summary>
    public static class SelectionReader
    {
        /// <summary>Items beyond this many are counted but not read, so a 50,000-item
        /// selection does not stall the UI before the request even starts.</summary>
        public const int MaxItemsRead = 200;

        public static string Summarise()
        {
            var read = Host.Selections.ReadTypesAndPropertyNames(MaxItemsRead);
            var rows = new System.Collections.Generic.List<PropertyRow>();
            foreach (var pair in read.properties)
                foreach (var name in pair.Value)
                    rows.Add(new PropertyRow { Category = pair.Key, Property = name });
            return SelectionSummary.Build(read.count, rows);
        }
    }
}
