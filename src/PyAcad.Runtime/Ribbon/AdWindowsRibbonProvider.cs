using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Windows;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Execution;

namespace PyAcad.Runtime.Ribbon
{
    /// <summary>
    /// Bundle-driven ribbon via AdWindows (Autodesk.Windows.ComponentManager).
    /// One RibbonTab per .tab folder, RibbonPanel per .panel, RibbonButton per
    /// .pushbutton; clicks dispatch to ScriptExecutor.
    /// </summary>
    public class AdWindowsRibbonProvider : IRibbonProvider
    {
        private static bool _subscribed;

        /// <summary>Hooks the icon refreshes onto the toggle events the first time a
        /// ribbon is built, not from a static constructor: RuntimeHost holds the provider in a
        /// static field, so any code reading RuntimeHost.UserConfigPath (updates, Settings, the
        /// secret store) would otherwise wire handlers that need AdWindows into tests.</summary>
        private static void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            ToggleStateStore.Changed += OnToggleChanged;
        }

        private static void OnToggleChanged(string bundleKey)
        {
            var item = ButtonRegistry.Find(bundleKey);
            if (item == null) return;
            var entry = ButtonRegistry.All.FirstOrDefault(e => e.model.BundleKey == bundleKey);
            var model = entry.model;
            if (model == null || !model.IsToggle) return;
            var on = ToggleStateStore.Get(bundleKey);
            var dark = Output.PyAcadTheme.IsDark;
            var path = on ? (dark ? model.OnDarkIconPath : model.OnIconPath)
                          : (dark ? model.OffDarkIconPath : model.OffIconPath);
            ApplyIcons(item, path, path);
            if (model.TitleOn != null || model.TooltipOn != null)
                ApplyCaption(item, model, item.Size != RibbonItemSize.Large, on);
        }

        /// <summary>Caption and tooltip for the state, markers and resolved chord included.</summary>
        private static void ApplyCaption(RibbonItem item, PushButtonModel model, bool small, bool on)
        {
            var binding = Input.ShortcutManager.BindingFor(model);
            item.Text = ButtonCaption.Text(model, small, on, binding != null);
            item.ToolTip = ButtonCaption.Tooltip(model, on, binding?.ToString());
        }

        private readonly List<RibbonTab> _createdTabs = new List<RibbonTab>();

        // If one of OUR tabs was active when Teardown ran (i.e. during a Reload),
        // re-activate its rebuilt counterpart so the ribbon doesn't jump to Home.
        private string _activeTabIdToRestore;

        public void Build(IReadOnlyList<ExtensionModel> extensions)
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                Log.Error("Build called but ComponentManager.Ribbon is null.");
                return;
            }

            Subscribe();
            var buttons = 0;
            // One bundle AdWindows rejects (or one tab) must cost only itself: the rest of
            // the ribbon still builds, and the user is told which ones are missing.
            var steps = new StepRunner();
            // Tabs and panels with the same name in two extensions are one (RibbonMerge),
            // which is how a user's own extension adds to the pyACAD tab.
            var mergedTabs = RibbonMerge.Tabs(extensions, note => Log.Info(note));
            steps.Each(mergedTabs, t => "Tab '" + t.Title + "'", tabModel =>
            {
                if (ribbon.FindTab(tabModel.Id) != null)
                {
                    Log.Error($"Ribbon tab '{tabModel.Id}' already exists - skipped (duplicate extension?).");
                    return;
                }

                var tab = new RibbonTab { Title = tabModel.Title, Id = tabModel.Id, KeyTip = "PY" };
                var tipTargets = new List<(string id, string title, string tip, RibbonItem item)>();
                foreach (var panelModel in tabModel.Panels)
                {
                    var source = new RibbonPanelSource { Title = panelModel.Title };
                    void Emit(IEnumerable<PanelItem> panelItems)
                    {
                    steps.Each(panelItems, DescribeItem, item =>
                    {
                        if (item is PushButtonModel buttonModel)
                        {
                            if (buttonModel.NoUi) return;
                            var ribbonButton = CreateButton(buttonModel);
                            source.Items.Add(ribbonButton);
                            if (buttonModel.IsSmart) SmartButtonInit.Run(buttonModel, ribbonButton);
                            tipTargets.Add((buttonModel.BundleKey ?? buttonModel.ScriptPath,
                                buttonModel.Title, buttonModel.KeyTipOverride, ribbonButton));
                            buttons++;
                        }
                        else if (item is StackModel stackModel)
                        {
                            source.Items.Add(CreateStack(stackModel, tipTargets));
                            buttons += stackModel.Buttons.Count;
                        }
                        else if (item is PulldownModel pulldownModel)
                        {
                            var ribbonPulldown = CreatePulldown(pulldownModel);
                            source.Items.Add(ribbonPulldown);
                            tipTargets.Add((pulldownModel.Directory,
                                pulldownModel.Title, pulldownModel.KeyTipOverride, ribbonPulldown));
                            buttons += pulldownModel.Buttons.Count;
                        }
                        else if (item is UrlButtonModel urlModel)
                        {
                            var urlButton = CreateSimpleButton(urlModel.Directory, urlModel.RibbonTitle, urlModel.Tooltip,
                                urlModel.IconPath, urlModel.DarkIconPath, urlModel.SmallIconPath, urlModel.SmallDarkIconPath,
                                () => OpenUrl(urlModel));
                            source.Items.Add(urlButton);
                            tipTargets.Add((urlModel.Directory, urlModel.Title, urlModel.KeyTipOverride, urlButton));
                            buttons++;
                        }
                        else if (item is LinkButtonModel linkModel)
                        {
                            var linkButton = CreateSimpleButton(linkModel.Directory, linkModel.RibbonTitle, linkModel.Tooltip,
                                linkModel.IconPath, linkModel.DarkIconPath, linkModel.SmallIconPath, linkModel.SmallDarkIconPath,
                                () => RunPlugin(linkModel));
                            source.Items.Add(linkButton);
                            tipTargets.Add((linkModel.Directory, linkModel.Title, linkModel.KeyTipOverride, linkButton));
                            buttons++;
                        }
                        else if (item is DockPaneModel paneModel)
                        {
                            var handle = Panes.PaletteRegistry.Find(paneModel.BundleKey);
                            if (handle == null)
                            {
                                // Its palette failed to build; that stage already said why.
                                Log.Info($"Dock pane '{paneModel.Title}' has no palette - no button.");
                                return;
                            }
                            var toggle = PaletteButtons.Create(paneModel, handle);
                            source.Items.Add(toggle);
                            tipTargets.Add((paneModel.BundleKey, paneModel.Title, paneModel.KeyTipOverride, toggle));
                            buttons++;
                        }
                    });
                    }

                    Emit(panelModel.Items);
                    if (panelModel.Slideout.Count > 0)
                    {
                        // Everything after the break lives in the flyout the host
                        // opens from the panel title ("Title v"), like the native
                        // Tags panel.
                        source.Items.Add(new RibbonPanelBreak());
                        Emit(panelModel.Slideout);
                    }
                    if (source.Items.Count > 0)
                        tab.Panels.Add(new RibbonPanel { Source = source });
                }

                if (tab.Panels.Count == 0) return;

                // Alt-navigation: deterministic keytips per tab (menus arrow-navigate,
                // so pulldown children get none).
                var tips = Input.KeyTips.Assign(
                    tipTargets.Select(t => (t.id, t.title, t.tip)).ToList());
                foreach (var target in tipTargets)
                    if (tips.TryGetValue(target.id, out var keyTip))
                        target.item.KeyTip = keyTip;

                // Tracked BEFORE it is added: if Add throws partway, Teardown still
                // removes whatever made it onto the ribbon, so the next Reload is not
                // refused by the duplicate-tab check above.
                _createdTabs.Add(tab);
                ribbon.Tabs.Add(tab);
            });
            Log.Info($"Ribbon built: {_createdTabs.Count} tab(s), {buttons} button(s).");

            var problems = steps.Summary();
            if (problems != null)
            {
                try { Forms.Toast.Show("error", "Some buttons could not be built", problems); }
                catch (Exception ex) { Log.Error("Could not show the ribbon failure toast", ex); }
            }

            if (_activeTabIdToRestore != null)
            {
                var restore = _createdTabs.Find(t => t.Id == _activeTabIdToRestore);
                _activeTabIdToRestore = null;
                if (restore != null)
                {
                    restore.IsActive = true;
                    Log.Info($"Re-activated tab '{restore.Title}' after rebuild.");
                }
            }
        }

        private static string DescribeItem(PanelItem item)
        {
            if (item is CaptionedPanelItem captioned) return "'" + captioned.Title + "'";
            if (item is StackModel stack) return "Stack '" + System.IO.Path.GetFileName(stack.Directory) + "'";
            return item.GetType().Name;
        }

        public bool TabsPresent()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return false;
            if (_createdTabs.Count == 0) return true;   // nothing to lose yet
            foreach (var tab in _createdTabs)
                if (ribbon.FindTab(tab.Id) == null) return false;
            return true;
        }

        public void Teardown()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            _activeTabIdToRestore = _createdTabs.Find(t => t.IsActive)?.Id;

            foreach (var tab in _createdTabs)
                ribbon.Tabs.Remove(tab);
            Log.Info($"Ribbon teardown: removed {_createdTabs.Count} tab(s).");
            _createdTabs.Clear();
            ButtonRegistry.Clear();
        }

        /// <summary>Two or three small buttons stacked in one panel column.</summary>
        private static RibbonRowPanel CreateStack(
            StackModel model, List<(string id, string title, string tip, RibbonItem item)> tipTargets)
        {
            var row = new RibbonRowPanel();
            for (var i = 0; i < model.Buttons.Count; i++)
            {
                if (i > 0) row.Items.Add(new RibbonRowBreak());
                var child = model.Buttons[i];
                var ribbonButton = CreateButton(child, small: true);
                row.Items.Add(ribbonButton);
                tipTargets.Add((child.BundleKey ?? child.ScriptPath,
                    child.Title, child.KeyTipOverride, ribbonButton));
            }
            return row;
        }

        /// <summary>
        /// A menu button: a split button with the split disabled, so the whole button
        /// opens the list and there is no default action.
        /// </summary>
        private static RibbonSplitButton CreatePulldown(PulldownModel model)
        {
            var pulldown = new RibbonSplitButton
            {
                Id = "PYACAD_PULL_" + Math.Abs(model.Directory.ToLowerInvariant().GetHashCode()),
                Text = model.RibbonTitle,
                ShowText = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                IsSplit = model.Kind != PulldownKind.Menu,
                IsSynchronizedWithCurrentItem = model.Kind == PulldownKind.SplitLastUsed,
                ListStyle = RibbonSplitButtonListStyle.List,
                // The list draws each child's image itself; Standard picks the
                // 16-slot art instead of blowing the large art up in the menu.
                ListImageSize = RibbonImageSize.Standard,
            };
            if (!string.IsNullOrEmpty(model.Tooltip))
                pulldown.ToolTip = model.Tooltip;

            var pulldownDark = Output.PyAcadTheme.IsDark;
            ApplyIcons(pulldown,
                pulldownDark ? model.DarkIconPath : model.IconPath,
                pulldownDark ? model.SmallDarkIconPath : model.SmallIconPath);

            foreach (var child in model.Buttons)
                pulldown.Items.Add(CreateButton(child, small: true));

            // A split header with no resolvable command falls through to
            // ComponentManager's default handler, which shows an error dialog.
            // LastUsed resolves through Current (synchronized); Fixed ignores
            // Current and needs its own handler for the first child.
            if (pulldown.Items.Count > 0)
            {
                if (model.Kind == PulldownKind.SplitLastUsed)
                {
                    pulldown.Current = (RibbonItem)pulldown.Items[0];
                }
                else if (model.Kind == PulldownKind.SplitFixed)
                {
                    var first = model.Buttons[0];
                    pulldown.CommandHandler = new RelayCommand(_ => Dispatch(first));
                }
            }
            return pulldown;
        }

        private static RibbonButton CreateButton(PushButtonModel model, bool small = false)
        {
            var button = new RibbonButton
            {
                Id = "PYACAD_BTN_" + Math.Abs(model.ScriptPath.ToLowerInvariant().GetHashCode()),
                ShowText = true,
                Size = small ? RibbonItemSize.Standard : RibbonItemSize.Large,
                Orientation = small
                    ? System.Windows.Controls.Orientation.Horizontal
                    : System.Windows.Controls.Orientation.Vertical,
                CommandHandler = new RelayCommand(_ => Dispatch(model)),
            };
            // Only the large vertical slot has a second line to wrap into; a stacked or
            // menu row draws left to right, so it stays flat. A bundle with a config.py
            // has a Shift+Click action, which nothing on the button would otherwise
            // reveal: the caption says so, then says a chord exists, and the tooltip
            // says which one. A toggle that is on keeps its on-state wording (title_on)
            // across a Reload. ButtonCaption holds the rules.
            var on = model.IsToggle && ToggleStateStore.Get(model.BundleKey);
            ApplyCaption(button, model, small, on);

            var dark = Output.PyAcadTheme.IsDark;
            ApplyIcons(button,
                dark ? model.DarkIconPath : model.IconPath,
                dark ? model.SmallDarkIconPath : model.SmallIconPath);
            if (on)
            {
                var onPath = dark ? model.OnDarkIconPath : model.OnIconPath;
                ApplyIcons(button, onPath, onPath);
            }
            ButtonRegistry.Register(model, button);
            return button;
        }

        /// <summary>A large button that runs an arbitrary action (url and link buttons).</summary>
        private static RibbonButton CreateSimpleButton(string directory, string title, string tooltip,
            string icon, string darkIcon, string smallIcon, string smallDarkIcon, Action onClick)
        {
            var button = new RibbonButton
            {
                Id = "PYACAD_BTN_" + Math.Abs(directory.ToLowerInvariant().GetHashCode()),
                Text = title,
                ShowText = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                CommandHandler = new RelayCommand(_ => onClick()),
            };
            if (!string.IsNullOrEmpty(tooltip)) button.ToolTip = tooltip;
            var dark = Output.PyAcadTheme.IsDark;
            ApplyIcons(button, dark ? darkIcon : icon, dark ? smallDarkIcon : smallIcon);
            return button;
        }

        private static void OpenUrl(UrlButtonModel model)
        {
            try { System.Diagnostics.Process.Start(model.Url); }
            catch (Exception ex)
            {
                Log.Error($"Could not open '{model.Url}'", ex);
                Forms.Toast.Show("error", "Could not open URL", model.Url);
            }
        }

        /// <summary>A link button names an AutoCAD command (native, LISP or another
        /// add-in's) in plugin: and sends it exactly as a typed command.</summary>
        private static void RunPlugin(LinkButtonModel model)
        {
            var name = CommandNames.Sanitize(model.PluginId);
            if (name.Length == 0 || CommandSender.Send(name)) return;
            Forms.Toast.Show("info", CommandDispatch.NoDrawingMessage, model.Title);
        }

        /// <summary>
        /// Ribbon clicks honor the modifier clicks; keyboard chords bypass this
        /// (ShortcutManager dispatches the command directly) so a chord's own
        /// Ctrl/Shift/Alt never reroutes the tool it triggers. Either way the bundle
        /// runs as its registered AutoCAD command, never from the click itself.
        /// </summary>
        private static void Dispatch(PushButtonModel model)
        {
            switch (ClickActions.For(System.Windows.Input.Keyboard.Modifiers))
            {
                case ClickAction.OpenFolder:
                    ClickActions.OpenFolder(model.ScriptPath);
                    break;
                case ClickAction.Config when model.ConfigScriptPath != null:
                    CommandDispatch.RunConfig(model);
                    break;
                case ClickAction.Config:
                    Forms.Toast.Show("info", "Shift+Click action not defined for this tool.", null);
                    break;
                default:
                    CommandDispatch.Run(model);
                    break;
            }
        }

        /// <summary>
        /// Large art goes to LargeImage, simplified small art to Image; AdWindows picks
        /// between them by the item's Size, so stacked and menu buttons get the glyph
        /// drawn for 16px. Each slot loads via RibbonIcons at ITS logical size, so a
        /// bundle shipping only icon.png still lands at 16 in the small slot instead
        /// of getting clipped. Theme is read once per ribbon build, and Reload rebuilds.
        /// </summary>
        internal static void ApplyIcons(RibbonItem item, string large, string small)
        {
            var largePath = large ?? small;
            var smallPath = small ?? large;
            var largeImage = largePath != null ? RibbonIcons.Load(largePath, 32) : null;
            var smallImage = smallPath != null ? RibbonIcons.Load(smallPath, 16) : null;

            if (largeImage == null && smallImage == null)
            {
                item.ShowImage = false;
                return;
            }
            item.LargeImage = largeImage ?? smallImage;
            item.Image = smallImage ?? largeImage;
            item.ShowImage = true;
        }
    }
}
