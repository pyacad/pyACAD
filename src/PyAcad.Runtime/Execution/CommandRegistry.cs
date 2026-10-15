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
        /// <summary>Every name acad.pgp defines (aliases and external commands); empty when
        /// the file cannot be found or read.</summary>
        ISet<string> ReadPgpNames();
        /// <summary>True when the active drawing's LISP defines C:name.</summary>
        bool IsLispCommand(string name);
        /// <summary>Queues (C:name) on the active drawing, to run when the current command ends.</summary>
        void RunLisp(string name);
    }

    /// <summary>
    /// Registers one AutoCAD command per bundle at boot and on every Reload, through
    /// AutoCAD's runtime command API rather than compile-time attributes, so a new bundle
    /// needs a Reload and never a restart. Commands run in document context, which is
    /// where AutoCAD holds the document lock and opens the undo group for free.
    ///
    /// Aliases never change what a keystroke already did: one that acad.pgp or AutoCAD
    /// already answers to is left out, and one a drawing's LISP defines later (acaddoc.lsp
    /// loads after boot) hands the run to that LISP command.
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

            var pgp = table.Assignments.Any(a => a.IsAlias) ? ReadPgp(host) : new HashSet<string>();
            foreach (var assignment in table.Assignments)
            {
                var name = assignment.Name;
                try
                {
                    if (assignment.IsAlias && pgp.Contains(name))
                    {
                        SkipAlias(assignment, "acad.pgp already defines it", problems);
                        continue;
                    }
                    if (host.IsNameInUse(name))
                    {
                        if (assignment.IsAlias)
                        {
                            SkipAlias(assignment, "AutoCAD already has a command by that name", problems);
                            continue;
                        }
                        problems.Add($"Command {name} belongs to something else in AutoCAD, so " +
                                     $"'{assignment.Model.BundleKey}' is ribbon-only. Set command: in bundle.yaml.");
                        continue;
                    }
                    var captured = assignment;
                    if (assignment.IsAlias)
                        host.Add(CommandNames.Group, name, () => RunAlias(host, captured, run));
                    else
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

        /// <summary>The user's own LISP command wins over an alias it was defined after.</summary>
        private static void RunAlias(ICommandHost host, CommandAssignment alias, Action<CommandAssignment> run)
        {
            var lisp = false;
            try { lisp = host.IsLispCommand(alias.Name); }
            catch (Exception ex) { Log.Error($"Could not look up LISP command {alias.Name}", ex); }
            if (lisp)
            {
                Log.Info($"Alias {alias.Name}: this drawing's LISP defines C:{alias.Name}, so that runs instead.");
                host.RunLisp(alias.Name);
                return;
            }
            run(alias);
        }

        /// <summary>A skipped alias costs nothing (the full name still works), so only the
        /// user's own aliases are worth a toast; a bundle's are a log line.</summary>
        private static void SkipAlias(CommandAssignment alias, string why, List<string> problems)
        {
            var text = $"Alias {alias.Name} for '{alias.Model.BundleKey}' is left out: {why}, " +
                       "and that keeps working as before.";
            if (alias.FromUser) problems.Add(text);
            else Log.Info(text);
        }

        private static ISet<string> ReadPgp(ICommandHost host)
        {
            try
            {
                return host.ReadPgpNames() ?? new HashSet<string>();
            }
            catch (Exception ex)
            {
                Log.Error("Could not read acad.pgp", ex);
                return new HashSet<string>();
            }
        }

        /// <summary>The registered name for a bundle, or null when it has none.</summary>
        public static string NameFor(PushButtonModel model, bool config = false)
        {
            var name = _table.NameFor(model, config);
            return name != null && Registered.Contains(name) ? name : null;
        }

        /// <summary>
        /// For the editor: what outside pyACAD already answers to a name ("in acad.pgp",
        /// "an AutoCAD command"), or null when it is free or is one of pyACAD's own. Null
        /// itself when there is no AutoCAD to ask.
        /// </summary>
        public static Func<string, string> OutsideOwnerCheck()
        {
            var host = ResolveHost();
            if (host == null) return null;
            var pgp = ReadPgp(host);
            var ours = new HashSet<string>(Registered, StringComparer.OrdinalIgnoreCase);
            return name =>
            {
                if (pgp.Contains(name)) return "in acad.pgp";
                if (ours.Contains(name)) return null;
                try { return host.IsNameInUse(name) ? "an AutoCAD command" : null; }
                catch (Exception ex)
                {
                    Log.Error($"Could not look up command {name}", ex);
                    return null;
                }
            };
        }

        /// <summary>The registered aliases for a bundle, in declaration order.</summary>
        public static IReadOnlyList<string> AliasesFor(PushButtonModel model) =>
            _table.AliasesFor(model).Select(a => a.Name).Where(Registered.Contains).ToList();

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
