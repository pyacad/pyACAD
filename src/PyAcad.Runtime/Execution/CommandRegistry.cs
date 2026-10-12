using System;
using System.Collections.Generic;
using System.Linq;
using PyAcad.Runtime.Bundles;

namespace PyAcad.Runtime.Execution
{
    /// <summary>What the registry needs from AutoCAD's command system; faked in tests.</summary>
    public interface ICommandHost
    {
        /// <summary>Defines a modal, document-context command that runs the callback.</summary>
        void Add(string group, string name, Action callback);
        void Remove(string group, string name);
        /// <summary>True when AutoCAD already answers to the name (a LISP or another add-in).</summary>
        bool IsNameInUse(string name);
    }

    /// <summary>
    /// Registers one AutoCAD command per bundle at boot and on every Reload, through
    /// AutoCAD's runtime command API rather than compile-time attributes, so a new bundle
    /// needs a Reload and never a restart. Commands run in document context, which is
    /// where AutoCAD holds the document lock and opens the undo group for free.
    /// </summary>
    public static class CommandRegistry
    {
        private static readonly List<string> Registered = new List<string>();
        private static CommandTable _table = CommandTable.Empty;

        /// <summary>Replaceable for tests; the real one talks to AutoCAD.</summary>
        internal static ICommandHost Host { get; set; }

        /// <summary>The current table: what the ribbon sends and what the LISP bridge looks up.</summary>
        public static CommandTable Table => _table;

        /// <summary>Names currently registered, in registration order.</summary>
        public static IReadOnlyList<string> RegisteredNames => Registered;

        /// <summary>
        /// Drops the previous set and registers the given one. Returns the problems to
        /// show: names the table could not give out plus names AutoCAD already owned.
        /// </summary>
        public static IReadOnlyList<string> Register(CommandTable table, Action<CommandAssignment> run)
        {
            Unregister();
            var host = ResolveHost();
            var problems = new List<string>(table.Problems);
            _table = table;
            if (host == null)
            {
                Log.Info("Command registration skipped: no command host in this session.");
                return problems;
            }

            foreach (var assignment in table.Assignments)
            {
                var name = assignment.Name;
                try
                {
                    if (host.IsNameInUse(name))
                    {
                        problems.Add($"Command {name} belongs to something else in AutoCAD, so " +
                                     $"'{assignment.Model.BundleKey}' is ribbon-only. Set command: in bundle.yaml.");
                        continue;
                    }
                    var captured = assignment;
                    host.Add(CommandNames.Group, name, () => run(captured));
                    Registered.Add(name);
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not register command {name}", ex);
                    problems.Add($"Command {name} could not be registered: {ex.Message}");
                }
            }
            Log.Info($"Commands registered: {Registered.Count} ({string.Join(", ", Registered)}).");
            return problems;
        }

        public static void Unregister()
        {
            var host = Registered.Count > 0 ? ResolveHost() : null;
            foreach (var name in Registered)
            {
                try { host?.Remove(CommandNames.Group, name); }
                catch (Exception ex) { Log.Error($"Could not remove command {name}", ex); }
            }
            Registered.Clear();
            _table = CommandTable.Empty;
        }

        /// <summary>The registered name for a bundle, or null when it has none.</summary>
        public static string NameFor(PushButtonModel model, bool config = false)
        {
            var name = _table.NameFor(model, config);
            return name != null && Registered.Contains(name) ? name : null;
        }

        private static ICommandHost ResolveHost()
        {
            if (Host != null) return Host;
            try
            {
                Host = Host ?? AcadCommandHost.TryCreate();
            }
            catch (Exception ex)
            {
                Log.Error("No AutoCAD command host available", ex);
            }
            return Host;
        }
    }
}
