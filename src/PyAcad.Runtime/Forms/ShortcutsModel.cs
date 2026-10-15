using System;
using System.Collections.Generic;
using System.Linq;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Config;
using PyAcad.Runtime.Execution;
using PyAcad.Runtime.Input;

namespace PyAcad.Runtime.Forms
{
    /// <summary>
    /// The shortcuts editor's state, kept free of WPF so every rule is unit-testable:
    /// one row per pushbutton in ribbon order, author defaults merged with config
    /// overrides, edits validated through ChordParser, conflicts flagged with the
    /// same first-wins order ShortcutMap uses, and a minimal override map out.
    /// All chord text is canonicalized (parse + Chord.ToString) so spelling
    /// variants of the same chord compare equal. Aliases ride along per row, checked
    /// with the rules CommandNames applies at registration: every full command name
    /// first, then aliases in ribbon order, first wins, and never a name acad.pgp or
    /// AutoCAD already answers to.
    /// </summary>
    public sealed class ShortcutsModel
    {
        public sealed class Row
        {
            internal Row() { }
            public string BundleKey { get; internal set; }
            public string PanelTitle { get; internal set; }
            public string Title { get; internal set; }
            /// <summary>Canonical author default, or null when the bundle ships none.</summary>
            public string DefaultChord { get; internal set; }
            /// <summary>Canonical current chord, or null when unbound/disabled.</summary>
            public string Current { get; internal set; }
            /// <summary>True when Current differs from the author default.</summary>
            public bool IsCustom => !string.Equals(Current, DefaultChord, StringComparison.Ordinal);
            /// <summary>Title of the earlier row that owns this chord, or null.</summary>
            public string ConflictWith { get; internal set; }

            /// <summary>The full command the tool answers to; an alias can never take one.</summary>
            public string CommandName { get; internal set; }
            /// <summary>Canonical author aliases ("BX, B2X"), or null when the bundle ships none.</summary>
            public string DefaultAliases { get; internal set; }
            /// <summary>Canonical current aliases, or null for none.</summary>
            public string CurrentAliases { get; internal set; }
            /// <summary>True when CurrentAliases differs from the author's.</summary>
            public bool IsAliasCustom => !string.Equals(CurrentAliases, DefaultAliases, StringComparison.Ordinal);
            /// <summary>True when the chord or the aliases differ from the author's.</summary>
            public bool IsAnyCustom => IsCustom || IsAliasCustom;
            /// <summary>Why one of the aliases will not work, or null.</summary>
            public string AliasProblem { get; internal set; }
            /// <summary>True when AliasProblem is about the user's own aliases, which blocks
            /// Save; an author alias that cannot work is only a note (registration skips it).</summary>
            public bool AliasBlocks => AliasProblem != null && IsAliasCustom;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly Dictionary<string, Row> _byKey =
            new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _loaded =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _loadedAliases =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly bool _allowBareKeys;
        private readonly Func<string, string> _aliasOwner;

        private ShortcutsModel(bool allowBareKeys, Func<string, string> aliasOwner)
        {
            _allowBareKeys = allowBareKeys;
            _aliasOwner = aliasOwner;
        }

        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>The config's shortcuts.allowBareKeys flag; recorders validate with it.</summary>
        public bool AllowBareKeys => _allowBareKeys;

        public bool HasConflicts => _rows.Any(r => r.ConflictWith != null);

        /// <summary>True while one of the user's own aliases cannot work.</summary>
        public bool HasAliasProblems => _rows.Any(r => r.AliasBlocks);

        /// <summary>True when any row differs from the state loaded from config.</summary>
        public bool IsDirty
        {
            get
            {
                foreach (var row in _rows)
                {
                    var had = _loaded.TryGetValue(row.BundleKey, out var was);
                    if (had != row.IsCustom) return true;
                    if (had && !string.Equals(was, row.Current, StringComparison.Ordinal)) return true;

                    var hadAliases = _loadedAliases.TryGetValue(row.BundleKey, out var wasAliases);
                    if (hadAliases != row.IsAliasCustom) return true;
                    if (hadAliases && !string.Equals(wasAliases, row.CurrentAliases, StringComparison.Ordinal)) return true;
                }
                return false;
            }
        }

        /// <summary><paramref name="aliasOwner"/> names what outside pyACAD already answers
        /// to a name ("in acad.pgp", "an AutoCAD command"), or returns null when the name is
        /// free; a null function checks nothing (outside AutoCAD).</summary>
        public static ShortcutsModel Build(
            IReadOnlyList<ExtensionModel> extensions, PyAcadConfig config,
            Func<string, string> aliasOwner = null)
        {
            var model = new ShortcutsModel(config.ShortcutsAllowBareKeys, aliasOwner);
            foreach (var panel in extensions.SelectMany(e => e.Tabs).SelectMany(t => t.Panels))
            {
                foreach (var button in panel.Buttons)
                {
                    if (button.BundleKey == null || model._byKey.ContainsKey(button.BundleKey))
                        continue;

                    var row = new Row
                    {
                        BundleKey = button.BundleKey,
                        PanelTitle = panel.Title,
                        Title = button.Title,
                        DefaultChord = model.Canonical(button.Shortcut),
                        CommandName = string.IsNullOrWhiteSpace(button.CommandName)
                            ? CommandNames.Derive(button.Title) : CommandNames.Sanitize(button.CommandName),
                        DefaultAliases = CanonicalAliases(string.Join(",", button.Aliases)),
                    };
                    // A user entry (even null) replaces the default entirely - same
                    // rule as ShortcutMap. Unparsable overrides fall back to default.
                    if (config.ShortcutBindings.TryGetValue(button.BundleKey, out var userChord))
                        row.Current = userChord == null ? null : model.Canonical(userChord) ?? row.DefaultChord;
                    else
                        row.Current = row.DefaultChord;

                    if (row.IsCustom)
                        model._loaded[row.BundleKey] = row.Current;

                    // Same rule as CommandNames.Assign: a user entry replaces the bundle's list.
                    row.CurrentAliases = config.Aliases.TryGetValue(button.BundleKey, out var mine)
                        ? CanonicalAliases(mine) : row.DefaultAliases;
                    if (row.IsAliasCustom)
                        model._loadedAliases[row.BundleKey] = row.CurrentAliases;
                    model._rows.Add(row);
                    model._byKey[row.BundleKey] = row;
                }
            }
            model.Reflag();
            return model;
        }

        /// <summary>Sets a row's aliases from what the user typed ("bx b2x", "BX, B2X");
        /// empty clears them. Returns null, or why the text holds no usable name.</summary>
        public string TrySetAliases(string bundleKey, string text)
        {
            if (!_byKey.TryGetValue(bundleKey, out var row)) return "unknown tool";
            var canonical = CanonicalAliases(text);
            if (canonical == null && !string.IsNullOrWhiteSpace(text))
                return "an alias needs letters or digits";
            row.CurrentAliases = canonical;
            Reflag();
            return null;
        }

        /// <summary>Rebinds a row; returns null on success or the parse error text.</summary>
        public string TrySet(string bundleKey, string chordText)
        {
            if (!_byKey.TryGetValue(bundleKey, out var row)) return "unknown tool";
            if (!ChordParser.TryParse(chordText, _allowBareKeys, out var chord, out var error))
                return error;

            row.Current = chord.ToString();
            Reflag();
            return null;
        }

        public void Disable(string bundleKey)
        {
            if (!_byKey.TryGetValue(bundleKey, out var row)) return;
            row.Current = null;
            Reflag();
        }

        /// <summary>Puts the row's chord and aliases back to the author's.</summary>
        public void ResetToDefault(string bundleKey)
        {
            if (!_byKey.TryGetValue(bundleKey, out var row)) return;
            row.Current = row.DefaultChord;
            row.CurrentAliases = row.DefaultAliases;
            Reflag();
        }

        public void ResetAll()
        {
            foreach (var row in _rows)
            {
                row.Current = row.DefaultChord;
                row.CurrentAliases = row.DefaultAliases;
            }
            Reflag();
        }

        /// <summary>The minimal override map: only rows away from their default;
        /// null means "disable the author default". Stale keys never survive.</summary>
        public Dictionary<string, string> ToBindings()
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in _rows)
                if (row.IsCustom && (row.Current != null || row.DefaultChord != null))
                    bindings[row.BundleKey] = row.Current;
            return bindings;
        }

        /// <summary>The config.json aliases map for the rows shown: only rows away from
        /// the author's list; null means "none, not even the author's".</summary>
        public Dictionary<string, string> ToAliases()
        {
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in _rows)
                if (row.IsAliasCustom)
                    aliases[row.BundleKey] = row.CurrentAliases;
            return aliases;
        }

        /// <summary>Every bundle key the editor showed. On save, aliases under other keys
        /// (an extension that is not loaded right now) are kept as they are.</summary>
        public IEnumerable<string> BundleKeys => _rows.Select(r => r.BundleKey);

        /// <summary>"bx b2x;BX" as "BX, B2X": sanitised, de-duplicated, null when empty.</summary>
        public static string CanonicalAliases(string text)
        {
            var names = CommandNames.SplitAliases(text).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return names.Count == 0 ? null : string.Join(", ", names);
        }

        private string Canonical(string chordText) =>
            chordText != null && ChordParser.TryParse(chordText, _allowBareKeys, out var chord, out _)
                ? chord.ToString() : null;

        /// <summary>First-wins conflict pass in ribbon order, matching ShortcutMap.</summary>
        private void Reflag()
        {
            var owners = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (var row in _rows)
            {
                row.ConflictWith = null;
                if (row.Current == null) continue;
                if (owners.TryGetValue(row.Current, out var winner))
                    row.ConflictWith = winner.Title;
                else
                    owners[row.Current] = row;
            }
            ReflagAliases();
        }

        /// <summary>CommandNames' order: full names first, then aliases in ribbon order.</summary>
        private void ReflagAliases()
        {
            var taken = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
                if (!string.IsNullOrEmpty(row.CommandName) && !taken.ContainsKey(row.CommandName))
                    taken[row.CommandName] = row;

            foreach (var row in _rows)
            {
                row.AliasProblem = null;
                foreach (var alias in CommandNames.SplitAliases(row.CurrentAliases))
                {
                    string problem = null;
                    if (taken.TryGetValue(alias, out var owner))
                    {
                        if (owner == row) continue;   // its own command name
                        problem = alias + " is taken by " + owner.Title;
                    }
                    else
                    {
                        var outside = _aliasOwner?.Invoke(alias);
                        if (!string.IsNullOrEmpty(outside)) problem = alias + " is " + outside + ", which keeps it";
                        else taken[alias] = row;
                    }
                    if (problem != null && row.AliasProblem == null) row.AliasProblem = problem;
                }
            }
        }
    }
}
