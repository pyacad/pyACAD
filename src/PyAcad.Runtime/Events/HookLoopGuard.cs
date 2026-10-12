using System.Collections.Generic;

namespace PyAcad.Runtime.Events
{
    /// <summary>
    /// Keeps a hook from running itself. An event raised while a hook runs (the hook
    /// renamed a layer, say) starts no hooks, so a hook cannot recurse until the stack
    /// overflows and takes AutoCAD with it. A debounced event a hook re-armed (it moved
    /// an object or changed the selection) fires flagged as hook-caused, which hooks skip,
    /// so an object-modified hook that edits an object does not loop four times a second. Other
    /// consumers, such as the ribbon's context: greying, still see every event.
    /// Pure so it is unit-testable; UI-thread only, like the events.
    /// </summary>
    public sealed class HookLoopGuard
    {
        private readonly HashSet<AcadEvent> _armedByHook = new HashSet<AcadEvent>();
        private bool _running;

        public bool Running => _running;

        /// <summary>False while a hook already runs.</summary>
        public bool TryEnter()
        {
            if (_running) return false;
            _running = true;
            return true;
        }

        public void Exit() => _running = false;

        /// <summary>A debounce timer was (re)started for <paramref name="evt"/>. The last
        /// arming decides: inside a hook marks the firing hook-caused, outside clears it.</summary>
        public void Armed(AcadEvent evt)
        {
            if (_running) _armedByHook.Add(evt);
            else _armedByHook.Remove(evt);
        }

        /// <summary>The debounce for <paramref name="evt"/> fired: true when a hook armed it
        /// last. Consumes the mark.</summary>
        public bool FiredFromHook(AcadEvent evt) => _armedByHook.Remove(evt);
    }
}
