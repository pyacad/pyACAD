using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PyAcad.Runtime.Config;
using PyAcad.Runtime.Output;
using Tokens = PyAcad.Runtime.Forms.DesignSystem.Tokens;

namespace PyAcad.Runtime.Forms
{
    /// <summary>
    /// The keyboard shortcuts editor, design system v2: one solid paper surface,
    /// panel names as hairline-divided section headers, a ChordRecorder per tool,
    /// accent only in the primary action and the recorder focus. A dumb view over
    /// ShortcutsModel; Build() constructs without showing (the unit-test seam),
    /// ShowAndSave() is the script-facing entry.
    /// </summary>
    public static class ShortcutsDialog
    {
        private sealed class RowUi
        {
            public ShortcutsModel.Row Row;
            public ChordRecorder Recorder;
            public TextBlock Badge;
            public Button Reset;
            public TextBlock ConflictCaption;
            public TextBox AliasBox;
            public TextBlock AliasCaption;
        }

        private const double AliasWidth = 90;

        private sealed class Parts
        {
            public ShortcutsModel Model;
            public Tokens T;
            public List<RowUi> RowUis = new List<RowUi>();
            public Button Save;
            public bool Saved;
            public TextBox Search;
            public StackPanel ListHost;
        }

        private static Parts PartsOf(Window window) => (Parts)window.Tag;

        /// <summary>
        /// Shows the editor for the live runtime state; writes config.json and
        /// returns true when the user saved changes (caller triggers Reload).
        /// </summary>
        public static bool ShowAndSave()
        {
            var config = PyAcadConfig.Load(RuntimeHost.UserConfigPath);
            var model = ShortcutsModel.Build(RuntimeHost.Extensions, config,
                Execution.CommandRegistry.OutsideOwnerCheck());
            var window = Build(model);
            window.ShowDialog();

            var parts = PartsOf(window);
            if (!parts.Saved) return false;

            try
            {
                PyAcadConfig.SaveShortcutBindings(RuntimeHost.UserConfigPath, model.ToBindings());
                PyAcadConfig.SaveAliases(RuntimeHost.UserConfigPath, model.ToAliases(), model.BundleKeys);
                return true;
            }
            catch (Exception ex)
            {
                // Locked-down profile, a second session mid-save, or a config.json the
                // user hand-edited into invalid JSON: say so instead of throwing out of
                // the click, and report "nothing saved" so the caller skips its Reload.
                Log.Error("Could not save shortcut bindings", ex);
                Toast.Show("error", "Shortcuts were not saved", ex.Message);
                return false;
            }
        }

        public static Window Build(ShortcutsModel model)
        {
            var parts = new Parts { Model = model, T = Tokens.For(PyAcadTheme.IsDark) };
            var t = parts.T;

            var window = new Window
            {
                Title = "Shortcuts and Aliases",
                Width = 660,
                Height = 640,
                MinWidth = 560,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Background = DesignSystem.Brush(t.Paper),
                Tag = parts,
                ShowInTaskbar = false,
            };
            FluentChrome.Apply(window);

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            body.Children.Add(Header(t));

            var search = SearchRow(parts);
            Grid.SetRow(search, 1);
            body.Children.Add(search);

            parts.ListHost = new StackPanel();
            parts.ListHost.Children.Add(BuildRows(parts));
            var scroll = new ScrollViewer
            {
                Content = parts.ListHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 0, 16, 0),
            };
            DesignSystem.SlimScroll(scroll);
            Grid.SetRow(scroll, 2);
            body.Children.Add(scroll);

            var footer = Footer(window, parts);
            Grid.SetRow(footer, 3);
            body.Children.Add(footer);

            window.Content = body;
            RefreshChrome(parts);
            return window;
        }

        // ---- test seams --------------------------------------------------------

        public static int RecorderCountOf(Window window) => PartsOf(window).RowUis.Count;

        public static int PanelHeaderCountOf(Window window) => PartsOf(window).RowUis
            .Select(r => r.Row.PanelTitle).Distinct().Count();

        public static bool SaveEnabledOf(Window window) => PartsOf(window).Save.IsEnabled;

        /// <summary>Types into the search box, as a user would.</summary>
        public static void SearchForTest(Window window, string query) =>
            PartsOf(window).Search.Text = query;

        public static int ConflictCaptionCountOf(Window window) => PartsOf(window).RowUis
            .Count(r => r.ConflictCaption.Visibility == Visibility.Visible);

        public static int AliasBoxCountOf(Window window) => PartsOf(window).RowUis.Count(r => r.AliasBox != null);

        public static int AliasCaptionCountOf(Window window) => PartsOf(window).RowUis
            .Count(r => r.AliasCaption.Visibility == Visibility.Visible);

        /// <summary>Types into a row's alias field, as a user would.</summary>
        public static void TypeAliasForTest(Window window, string bundleKey, string text) =>
            PartsOf(window).RowUis.First(r => r.Row.BundleKey == bundleKey).AliasBox.Text = text;

        // ---- construction ------------------------------------------------------

        private static FrameworkElement Header(Tokens t)
        {
            var stack = new StackPanel { Margin = new Thickness(16, 14, 16, 10) };
            stack.Children.Add(DesignSystem.Text("Shortcuts and Aliases", 16, t.Ink, FontWeights.SemiBold));
            var caption = DesignSystem.Text(
                "Click a shortcut field and press the keys. Backspace clears a shortcut, Esc leaves it as it was. " +
                "An alias is a short name you type, like BX; separate several with commas.",
                12, t.Muted);
            caption.Margin = new Thickness(0, 4, 0, 0);
            caption.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(caption);
            return stack;
        }

        private static FrameworkElement SearchRow(Parts parts)
        {
            parts.Search = new TextBox { FontSize = 13 };
            parts.Search.TextChanged += (s, e) => RefreshList(parts);
            var field = DesignSystem.InputField(parts.T, parts.Search);
            field.Margin = new Thickness(16, 0, 16, 10);
            return field;
        }

        /// <summary>Rebuilds the list for the current filter. Every extension's
        /// bundles land here, so a search box is the difference between finding
        /// a tool and scrolling for it.</summary>
        private static void RefreshList(Parts parts)
        {
            parts.RowUis.Clear();
            parts.ListHost.Children.Clear();
            parts.ListHost.Children.Add(BuildRows(parts));
            RefreshChrome(parts);
        }

        private static StackPanel BuildRows(Parts parts)
        {
            var t = parts.T;
            var list = new StackPanel();
            var query = (parts.Search?.Text ?? "").Trim();

            var visible = parts.Model.Rows.Where(r =>
                query.Length == 0
                || (r.Title ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (r.PanelTitle ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (r.Current ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (r.CurrentAliases ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (r.CommandName ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (visible.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(0, 40, 0, 0) };
                var big = DesignSystem.Text(
                    parts.Model.Rows.Count == 0 ? "No tools to bind yet" : "No tools match",
                    13, t.Ink, FontWeights.SemiBold);
                big.HorizontalAlignment = HorizontalAlignment.Center;
                var sub = DesignSystem.Text(
                    parts.Model.Rows.Count == 0
                        ? "Shortcuts appear here once an extension ships a tool."
                        : "Try a shorter search.",
                    12, t.Muted);
                sub.HorizontalAlignment = HorizontalAlignment.Center;
                sub.Margin = new Thickness(0, 4, 0, 0);
                empty.Children.Add(big);
                empty.Children.Add(sub);
                list.Children.Add(empty);
                return list;
            }

            var first = true;
            foreach (var group in visible.GroupBy(r => r.PanelTitle))
            {
                var header = new Border
                {
                    BorderBrush = DesignSystem.Brush(t.Line),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Margin = new Thickness(0, 14, 0, 4),
                    Padding = new Thickness(0, 0, 0, 4),
                    Child = PanelHeader(t, group.Key, columnLabels: first),
                };
                first = false;
                list.Children.Add(header);

                foreach (var row in group)
                    list.Children.Add(BuildRow(parts, row));
            }
            return list;
        }

        /// <summary>The panel name, and over the first panel only, the two column labels,
        /// right-anchored like the fields in every row below.</summary>
        private static FrameworkElement PanelHeader(Tokens t, string panel, bool columnLabels)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8 + AliasWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.Children.Add(DesignSystem.Text(panel, 12, t.Muted, FontWeights.Medium));
            if (!columnLabels) return grid;

            var shortcut = DesignSystem.Text("Shortcut", 12, t.Muted);
            Grid.SetColumn(shortcut, 1);
            grid.Children.Add(shortcut);
            var alias = DesignSystem.Text("Alias", 12, t.Muted);
            alias.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(alias, 2);
            grid.Children.Add(alias);
            return grid;
        }

        private static FrameworkElement BuildRow(Parts parts, ShortcutsModel.Row row)
        {
            var t = parts.T;
            var ui = new RowUi { Row = row };
            parts.RowUis.Add(ui);

            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = DesignSystem.Text(row.Title, 13, t.Ink);
            title.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(title);

            ui.Badge = DesignSystem.Text("", 11.5, t.Muted);   // 11.5 is the scale floor
            ui.Badge.VerticalAlignment = VerticalAlignment.Center;
            ui.Badge.Margin = new Thickness(8, 0, 10, 0);
            Grid.SetColumn(ui.Badge, 1);
            grid.Children.Add(ui.Badge);

            ui.Recorder = new ChordRecorder(AllowBareKeysOf(parts.Model)) { ChordText = row.Current };
            Grid.SetColumn(ui.Recorder, 2);
            grid.Children.Add(ui.Recorder);

            // Committed on every keystroke, so Save lights up without leaving the field;
            // the text is tidied to its canonical form ("BX, B2X") only on the way out.
            ui.AliasBox = new TextBox
            {
                Height = 24,   // plus the field border: level with the 26px recorder
                Text = row.CurrentAliases ?? "",
                CharacterCasing = CharacterCasing.Upper,
                ToolTip = "Runs " + row.CommandName,
            };
            var aliasField = DesignSystem.InputField(t, ui.AliasBox);
            aliasField.Width = AliasWidth;
            aliasField.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(aliasField, 3);
            grid.Children.Add(aliasField);
            ui.AliasBox.TextChanged += (s, e) =>
            {
                parts.Model.TrySetAliases(row.BundleKey, ui.AliasBox.Text);
                RefreshChrome(parts);
            };
            ui.AliasBox.LostKeyboardFocus += (s, e) => ui.AliasBox.Text = row.CurrentAliases ?? "";

            ui.Reset = ResetButton(t, () =>
            {
                parts.Model.ResetToDefault(row.BundleKey);
                ui.Recorder.ChordText = row.Current;
                ui.AliasBox.Text = row.CurrentAliases ?? "";
                RefreshChrome(parts);
            });
            Grid.SetColumn(ui.Reset, 4);
            grid.Children.Add(ui.Reset);

            // Error copy reads at the start of the row, at the 12px caption size:
            // 11px right-aligned put the explanation furthest from the name it
            // belongs to.
            var captions = new StackPanel();
            ui.ConflictCaption = DesignSystem.Text("", 12, t.Error);
            ui.ConflictCaption.Margin = new Thickness(0, 1, 0, 2);
            captions.Children.Add(ui.ConflictCaption);
            ui.AliasCaption = DesignSystem.Text("", 12, t.Muted);
            ui.AliasCaption.Margin = new Thickness(0, 1, 0, 2);
            captions.Children.Add(ui.AliasCaption);
            Grid.SetRow(captions, 1);
            Grid.SetColumnSpan(captions, 5);
            grid.Children.Add(captions);

            ui.Recorder.ChordChanged += chord =>
            {
                if (chord == null) parts.Model.Disable(row.BundleKey);
                else
                {
                    var error = parts.Model.TrySet(row.BundleKey, chord);
                    if (error != null) ui.Recorder.ChordText = row.Current; // parser refused: revert
                }
                RefreshChrome(parts);
            };
            return grid;
        }

        /// <summary>Drawn circular-arrow reset glyph; visible only on customized rows.</summary>
        private static Button ResetButton(Tokens t, Action onClick)
        {
            var arrow = new Path
            {
                Data = Geometry.Parse("M 9,1 A 5,5 0 1 0 11,5"),
                Stroke = DesignSystem.Brush(t.Muted),
                StrokeThickness = 1.4,
                Width = 12,
                Height = 12,
                Stretch = Stretch.None,
            };
            var button = new Button
            {
                Content = arrow,
                Width = 24,
                Height = 24,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Template = DesignSystem.ButtonChrome(new CornerRadius(3)),
                FocusVisualStyle = DesignSystem.FocusRing(DesignSystem.Accent),
                ToolTip = "Reset to default",
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            button.Click += (s, e) => onClick();
            return button;
        }

        private static FrameworkElement Footer(Window window, Parts parts)
        {
            var t = parts.T;
            var bar = new Grid { Margin = new Thickness(16, 12, 16, 16) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var restore = DesignSystem.Quiet(t, "Restore All Defaults", () =>
            {
                parts.Model.ResetAll();
                foreach (var ui in parts.RowUis)
                {
                    ui.Recorder.ChordText = ui.Row.Current;
                    ui.AliasBox.Text = ui.Row.CurrentAliases ?? "";
                }
                RefreshChrome(parts);
            });
            restore.HorizontalAlignment = HorizontalAlignment.Left;
            bar.Children.Add(restore);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var cancel = DesignSystem.Secondary(t, "Cancel", () => window.Close());
            cancel.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(cancel);

            parts.Save = DesignSystem.Primary(t, "Save", () =>
            {
                parts.Saved = true;
                window.Close();
            });
            actions.Children.Add(parts.Save);
            Grid.SetColumn(actions, 1);
            bar.Children.Add(actions);

            var frame = new Border
            {
                BorderBrush = DesignSystem.Brush(t.Line),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = bar,
            };
            return frame;
        }

        // ---- state -> chrome ---------------------------------------------------

        private static void RefreshChrome(Parts parts)
        {
            foreach (var ui in parts.RowUis)
            {
                var row = ui.Row;
                ui.Badge.Text = !row.IsAnyCustom ? ""
                    : row.IsCustom && row.Current == null ? "disabled" : "custom";
                ui.Reset.Visibility = (row.IsCustom && row.DefaultChord != null) || row.IsAliasCustom
                    ? Visibility.Visible : Visibility.Hidden;

                var conflict = row.ConflictWith != null;
                ui.ConflictCaption.Text = conflict ? "conflicts with " + row.ConflictWith : "";
                ui.ConflictCaption.Visibility = conflict ? Visibility.Visible : Visibility.Collapsed;

                // The user's own alias that cannot work blocks Save, in the error colour;
                // an author's alias that cannot work is a quiet note: it is skipped, and
                // the full command still runs the tool.
                var aliasProblem = row.AliasProblem != null;
                ui.AliasCaption.Text = !aliasProblem ? ""
                    : row.AliasBlocks ? row.AliasProblem : "Alias not active: " + row.AliasProblem;
                ui.AliasCaption.Foreground = DesignSystem.Brush(row.AliasBlocks ? parts.T.Error : parts.T.Muted);
                ui.AliasCaption.Visibility = aliasProblem ? Visibility.Visible : Visibility.Collapsed;
            }
            parts.Save.IsEnabled = parts.Model.IsDirty && !parts.Model.HasConflicts && !parts.Model.HasAliasProblems;
        }

        private static bool AllowBareKeysOf(ShortcutsModel model) => model.AllowBareKeys;
    }
}
