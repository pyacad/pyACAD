using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PyAcad.Runtime.Bundles;

namespace PyAcad.Runtime.Execution
{
    /// <summary>One registered AutoCAD command: the name the user types and the bundle it runs.</summary>
    public sealed class CommandAssignment
    {
        public string Name { get; }
        public PushButtonModel Model { get; }
        /// <summary>True for the Shift+Click twin that runs the bundle's config.py.</summary>
        public bool IsConfig { get; }

        public CommandAssignment(string name, PushButtonModel model, bool isConfig)
        {
            Name = name;
            Model = model;
            IsConfig = isConfig;
        }
    }

    /// <summary>The outcome of naming every bundle: the table plus what did not fit.</summary>
    public sealed class CommandTable
    {
        private readonly Dictionary<string, CommandAssignment> _byName =
            new Dictionary<string, CommandAssignment>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<CommandAssignment> Assignments { get; }
        public IReadOnlyList<string> Problems { get; }

        public CommandTable(IReadOnlyList<CommandAssignment> assignments, IReadOnlyList<string> problems)
        {
            Assignments = assignments;
            Problems = problems;
            foreach (var a in assignments) _byName[a.Name] = a;
        }

        public static CommandTable Empty { get; } = new CommandTable(new CommandAssignment[0], new string[0]);

        public CommandAssignment Find(string name) =>
            name != null && _byName.TryGetValue(name, out var a) ? a : null;

        /// <summary>The command a bundle answers to, or null when it got none.</summary>
        public string NameFor(PushButtonModel model, bool config = false) =>
            Assignments.FirstOrDefault(a => a.Model == model && a.IsConfig == config)?.Name;
    }

    /// <summary>
    /// Every pushbutton bundle is an AutoCAD command, so a tool can be clicked, typed,
    /// scripted, put in a macro or called from AutoLISP through one code path. Names come
    /// from bundle.yaml <c>command:</c> or are derived from the title; first wins on a
    /// clash, and the loser is reported so the author sees it rather than wondering why
    /// typing the name runs a different tool.
    /// </summary>
    public static class CommandNames
    {
        public const string Prefix = "PY";
        public const string ConfigSuffix = "CFG";
        public const string Fallback = "PYTOOL";
        public const string Group = "PYACAD";

        /// <summary>AutoCAD accepts long names, but a typed command should stay readable.</summary>
        public const int MaxLength = 64;

        /// <summary>Commands the loader defines statically; a bundle may not take them.</summary>
        public static readonly string[] Builtins =
        {
            "PYACAD", "PYACADRELOAD", "PYACADCONSOLE", "PYACADRUN", "PYACADSETTINGS",
        };

        /// <summary>"Blocks to Excel" becomes PYBLOCKSTOEXCEL.</summary>
        public static string Derive(string title)
        {
            var body = new StringBuilder();
            foreach (var ch in title ?? "")
                if (char.IsLetterOrDigit(ch)) body.Append(char.ToUpperInvariant(ch));
            if (body.Length == 0) return Fallback;
            return Clip(Prefix + body);
        }

        /// <summary>Uppercases and drops anything AutoCAD would not accept in a command name.</summary>
        public static string Sanitize(string raw)
        {
            var body = new StringBuilder();
            foreach (var ch in raw ?? "")
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') body.Append(char.ToUpperInvariant(ch));
            return Clip(body.ToString());
        }

        private static string Clip(string name) =>
            name.Length > MaxLength ? name.Substring(0, MaxLength) : name;

        /// <summary>Names every button, config twins included, first wins on duplicates.</summary>
        public static CommandTable Assign(IEnumerable<PushButtonModel> buttons)
        {
            var taken = new HashSet<string>(Builtins, StringComparer.OrdinalIgnoreCase);
            var assignments = new List<CommandAssignment>();
            var problems = new List<string>();

            foreach (var model in buttons)
            {
                var wanted = string.IsNullOrWhiteSpace(model.CommandName)
                    ? Derive(model.Title)
                    : Sanitize(model.CommandName);
                if (wanted.Length == 0) wanted = Fallback;

                if (!taken.Add(wanted))
                {
                    problems.Add($"Command {wanted} is already taken, so '{model.BundleKey ?? model.ScriptPath}' " +
                                 "has no command. Give it a different command: in bundle.yaml.");
                    continue;
                }
                assignments.Add(new CommandAssignment(wanted, model, isConfig: false));

                if (model.ConfigScriptPath == null) continue;
                var cfg = Clip(wanted + ConfigSuffix);
                if (!taken.Add(cfg))
                {
                    problems.Add($"Command {cfg} is already taken, so the config script of " +
                                 $"'{model.BundleKey ?? model.ScriptPath}' has no command.");
                    continue;
                }
                assignments.Add(new CommandAssignment(cfg, model, isConfig: true));
            }
            return new CommandTable(assignments, problems);
        }
    }
}
