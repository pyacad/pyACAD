using System;
using System.Collections.Generic;
using System.Linq;
using PyAcad.Runtime.Bundles;

namespace PyAcad.Runtime.Events
{
    /// <summary>The pure half of event wiring: which hub event a host signal becomes.</summary>
    public static class AcadEventMap
    {
        /// <summary>AutoCAD raises one DocumentCreated for a new drawing and for an opened
        /// file alike; a named drawing came from disk.</summary>
        public static AcadEvent DocumentArrival(bool isNamedDrawing) =>
            isNamedDrawing ? AcadEvent.DocOpened : AcadEvent.DocCreated;

        /// <summary>CLAYER is the current layer; no other variable is a layer change.</summary>
        public static AcadEvent SystemVariable(string name) =>
            string.Equals(name, "CLAYER", StringComparison.OrdinalIgnoreCase)
                ? AcadEvent.LayerChanged
                : AcadEvent.SystemVariableChanged;

        /// <summary>The events a drawing fires per entity, which only a hook that wants
        /// them should make pyACAD pay for.</summary>
        public static readonly AcadEvent[] ObjectEvents =
        {
            AcadEvent.ObjectAppended, AcadEvent.ObjectModified, AcadEvent.ObjectErased,
        };

        /// <summary>True when any extension has a hook for one of the object events.</summary>
        public static bool WantsObjectEvents(IEnumerable<ExtensionModel> extensions)
        {
            var names = new HashSet<string>(ObjectEvents.Select(EventNames.NameOf), StringComparer.OrdinalIgnoreCase);
            return extensions.SelectMany(e => e.Hooks).Any(h => names.Contains(h.EventName));
        }
    }
}
