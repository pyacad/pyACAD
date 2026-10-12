using System;
using System.Collections.Generic;

namespace PyAcad.Runtime.Events
{
    /// <summary>
    /// The one place that touches AutoCAD API events. Wire() subscribes once per
    /// session, normalizes to AcadEvent, debounces the chatty ones, and fans out to
    /// consumers (ContextGate, HookRunner, toggle refresh). UI thread only: API events
    /// arrive there and consumers must stay there because the API is not thread-safe.
    /// </summary>
    public static class AcadEvents
    {
        public const int DebounceMs = 250;

        /// <summary>Events that can fire many times for one user action. Notify coalesces
        /// them, so consumers see where things came to rest, not every step on the way.</summary>
        public static readonly IReadOnlyCollection<AcadEvent> Debounced = new[]
        {
            AcadEvent.SelectionChanged,
            AcadEvent.ObjectModified,
            AcadEvent.SystemVariableChanged,
        };

        /// <summary>All consumers hang off this one multiplexed event.</summary>
        public static event Action<AcadEventArgs> Raised;

        private static readonly PendingEvents Pending = new PendingEvents();
        private static readonly Dictionary<AcadEvent, System.Windows.Forms.Timer> Timers =
            new Dictionary<AcadEvent, System.Windows.Forms.Timer>();

        private static bool _wired;

        /// <summary>
        /// AutoCAD fires the object events once per entity, thousands of times for one
        /// block insert or regen, so they only reach the hub when some extension ships a
        /// hook for one of them (RuntimeHost sets this from the hook list on every Reload).
        /// </summary>
        public static bool ObjectEventsEnabled { get; set; }

        public static void Wire()
        {
            if (_wired) return;
            _wired = true;
            foreach (var evt in Debounced) Timers[evt] = MakeTimer(evt);
            try
            {
                AcadEventSource.Subscribe();
                Log.Info("AcadEvents wired to the AutoCAD application, document and database events.");
            }
            catch (Exception ex)
            {
                // No AutoCAD in this process (tests) or a headless one: the hub still
                // works for the events pyACAD raises itself (app-init, before/after-command).
                Log.Info("AcadEvents wired without host events: " + ex.GetType().Name);
            }
        }

        public static void Unwire()
        {
            if (!_wired) return;
            _wired = false;
            try { AcadEventSource.Unsubscribe(); }
            catch (Exception ex) { Log.Info("Host events not unsubscribed: " + ex.GetType().Name); }
            foreach (var timer in Timers.Values) timer.Dispose();
            Timers.Clear();
        }

        /// <summary>Object events, dropped unless a hook asked for them.</summary>
        public static void NotifyObject(AcadEvent evt)
        {
            if (!ObjectEventsEnabled) return;
            Notify(evt);
        }

        /// <summary>
        /// Entry point for host event handlers: a debounced event restarts its timer and
        /// is raised once things settle; any other event is raised straight away.
        /// </summary>
        public static void Notify(AcadEvent evt)
        {
            if (Timers.TryGetValue(evt, out var timer))
            {
                HookRunner.Loop.Armed(evt);
                timer.Stop();
                timer.Start();
                return;
            }
            Raise(evt);
        }

        /// <summary>Raises to all consumers; one failing consumer never starves the rest.</summary>
        public static void Raise(AcadEvent evt, string commandKey = null, bool fromHook = false)
        {
            var handlers = Raised;
            if (handlers == null) return;
            var args = new AcadEventArgs
            {
                Event = evt,
                CommandKey = commandKey,
                FromHook = fromHook,
            };
            foreach (Action<AcadEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(args); }
                catch (Exception ex) { Log.Error($"Event consumer failed on '{args.Name}'", ex); }
            }
        }

        /// <summary>
        /// Raises when nothing is running, otherwise holds the event until the command
        /// finishes. Reload raises app-init from INSIDE the Reload command, and HookRunner
        /// skips hooks while the gate is held (a hook's Python must not run nested in the
        /// outer script's engine run), so a plain Raise there is silently dropped.
        /// </summary>
        public static void RaiseWhenIdle(AcadEvent evt)
        {
            if (!Execution.RunGate.IsHeld)
            {
                Raise(evt);
                return;
            }
            Pending.Add(evt);
            Log.Info($"Event '{EventNames.NameOf(evt)}' held until " +
                     $"'{Execution.RunGate.CurrentTitle}' finishes.");
        }

        /// <summary>Replays whatever RaiseWhenIdle held back. Called once the gate frees.</summary>
        public static void FlushPending()
        {
            foreach (var evt in Pending.Take()) Raise(evt);
        }

        private static System.Windows.Forms.Timer MakeTimer(AcadEvent evt)
        {
            var timer = new System.Windows.Forms.Timer { Interval = DebounceMs };
            timer.Tick += (s, e) => { timer.Stop(); Raise(evt, fromHook: HookRunner.Loop.FiredFromHook(evt)); };
            return timer;
        }
    }
}
