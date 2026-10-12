using System.Collections.Generic;

namespace PyAcad.Runtime.Events
{
    /// <summary>Normalized pyACAD events; hooks and gating consume these, never raw API events.</summary>
    public enum AcadEvent
    {
        DocCreated, DocOpened, DocActivated, DocClosing, DocSaved,
        CommandWillStart, CommandEnded, CommandCancelled, CommandFailed,
        BeforeCommand, AfterCommand,
        SelectionChanged,
        ObjectAppended, ObjectModified, ObjectErased,
        LayerChanged, LayoutSwitched,
        AppInit, AppQuit,
        SystemVariableChanged,
    }

    /// <summary>Kebab-case wire names: hook filenames and __event__["name"] use these.</summary>
    public static class EventNames
    {
        private static readonly Dictionary<AcadEvent, string> Names = new Dictionary<AcadEvent, string>
        {
            [AcadEvent.DocCreated] = "doc-created",
            [AcadEvent.DocOpened] = "doc-opened",
            [AcadEvent.DocActivated] = "doc-activated",
            [AcadEvent.DocClosing] = "doc-closing",
            [AcadEvent.DocSaved] = "doc-saved",
            // command-* are AutoCAD's own commands; before-command and after-command
            // wrap a pyACAD ribbon button run (ScriptExecutor raises them).
            [AcadEvent.CommandWillStart] = "command-will-start",
            [AcadEvent.CommandEnded] = "command-ended",
            [AcadEvent.CommandCancelled] = "command-cancelled",
            [AcadEvent.CommandFailed] = "command-failed",
            [AcadEvent.BeforeCommand] = "before-command",
            [AcadEvent.AfterCommand] = "after-command",
            [AcadEvent.SelectionChanged] = "selection-changed",
            [AcadEvent.ObjectAppended] = "object-appended",
            [AcadEvent.ObjectModified] = "object-modified",
            [AcadEvent.ObjectErased] = "object-erased",
            [AcadEvent.LayerChanged] = "layer-changed",
            [AcadEvent.LayoutSwitched] = "layout-switched",
            [AcadEvent.AppInit] = "app-init",
            [AcadEvent.AppQuit] = "app-quit",
            [AcadEvent.SystemVariableChanged] = "system-variable-changed",
        };

        private static readonly Dictionary<string, AcadEvent> ByName = BuildInverse();

        private static Dictionary<string, AcadEvent> BuildInverse()
        {
            var map = new Dictionary<string, AcadEvent>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Names) map[pair.Value] = pair.Key;
            return map;
        }

        public static string NameOf(AcadEvent evt) => Names[evt];

        public static bool TryParse(string name, out AcadEvent evt)
        {
            if (name != null) return ByName.TryGetValue(name, out evt);
            evt = default(AcadEvent);
            return false;
        }

        public static IEnumerable<string> All => Names.Values;
    }

    public class AcadEventArgs
    {
        public AcadEvent Event { get; set; }
        public string Name => EventNames.NameOf(Event);
        /// <summary>Bundle key of the command for Before/AfterCommand; null otherwise.</summary>
        public string CommandKey { get; set; }

        /// <summary>A debounced event a running hook caused (HookLoopGuard); hooks skip it.</summary>
        public bool FromHook { get; set; }
    }
}
