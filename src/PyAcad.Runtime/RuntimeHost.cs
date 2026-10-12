using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Config;
using PyAcad.Runtime.Install;
using PyAcad.Runtime.Engine;
using PyAcad.Runtime.Ribbon;

namespace PyAcad.Runtime
{
    /// <summary>
    /// Runtime entry points, called (via reflection-safe seams) from the loader.
    /// </summary>
    public static class RuntimeHost
    {
        private static readonly IRibbonProvider RibbonProvider = new AdWindowsRibbonProvider();
        private static string ApiVersion => PyAcadVersion.HostApiVersion;

        /// <summary>Host release year (2023-2027), for min/max_host_version gating.</summary>
        public static int HostYear => PyAcadVersion.HostYear;

        private static bool _booted;
        private static bool _startupScriptsRan;
        private static List<ExtensionModel> _extensions = new List<ExtensionModel>();
        private static List<string> _libraries = new List<string>();

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyACAD", "config.json");

        /// <summary>The parsed extensions behind the current ribbon (Shortcuts editor reads these).</summary>
        public static IReadOnlyList<ExtensionModel> Extensions => _extensions;

        /// <summary>The user config.json path, for features that write settings back.</summary>
        public static string UserConfigPath => ConfigPath;

        /// <summary>Called once at AutoCAD startup from Loader.Initialize.</summary>
        public static void Boot()
        {
            if (_booted) return;
            _booted = true;

            Log.Info("==== pyACAD runtime booting ====");
            Log.Info(PyAcadVersion.Summary());
            Log.Info($"AutoCAD API assembly: {ApiVersion}");
            var automated = Execution.Runner.AutomatedSession.Detect();

            var userConfig = PyAcadConfig.Load(ConfigPath);
            var runtimeDir = Path.GetDirectoryName(typeof(RuntimeHost).Assembly.Location);
            _libraries = LoadLibraries(userConfig);
            EngineManager.Configure(EngineConfigBuilder.Build(runtimeDir, userConfig, _libraries));
            _extensions = LoadExtensions(userConfig);

            if (automated && userConfig.AutomatedBootFull)
            {
                Log.Info("automatedBoot is \"full\" in config.json: booting the interactive stages in this automated session.");
                automated = false;
            }

            // A hidden pyacad run session builds none of it: no one is there to use a
            // ribbon or read a notice, and startup.py is the runner's to start.
            // Core Console has no ribbon to wait for (and no AdWindows to ask).
            Action<Action> whenRibbonReady = Host.HostInfo.IsCoreConsole
                ? (Action<Action>)(build => Log.Info("Core Console: no ribbon, interactive stages skipped."))
                : RibbonBootstrap.WhenRibbonReady;
            Execution.Runner.AutomatedSession.QueueInteractiveStages(automated, whenRibbonReady, () =>
            {
                var steps = new StepRunner();
                RunStartupStages(steps, userConfig);
                steps.Run("App-init event", () => Events.AcadEvents.RaiseWhenIdle(Events.AcadEvent.AppInit));
                // What the last update kept of the user's work, and a warning when there
                // is work in pyACAD.extension, which the next update replaces.
                steps.Run("Upgrade notices", () =>
                {
                    var appData = Path.GetDirectoryName(ConfigPath);
                    Upgrade.UpgradeNotices.ShowLastUpgradeReport(appData);
                    Upgrade.UpgradeNotices.WarnAboutWorkInShippedExtension(_extensions, appData);
                });
                // Once a day, in the background: is a newer release out? (Settings > Updates)
                steps.Run("Update check", () =>
                    Update.UpdateService.Start(userConfig, Path.GetDirectoryName(ConfigPath)));
                ReportFailures(steps, "pyACAD started with problems");
            });

            Log.Info("Boot sequence complete (ribbon build may be deferred until ribbon exists).");
        }

        /// <summary>
        /// Runs every extension's startup.py unless Boot, Reload or an earlier call already
        /// has since the last Reload. Boot queues startup scripts behind the ribbon, and in
        /// the hidden AutoCAD that pyacad run drives the ribbon may never be ready, so the
        /// runner calls this before each script.
        /// </summary>
        public static void EnsureStartupScriptsRan()
        {
            if (_startupScriptsRan) return;
            Execution.StartupRunner.RunAll(_extensions);
            _startupScriptsRan = true;
        }

        /// <summary>Rescans extension folders and rebuilds the ribbon. No restart needed.</summary>
        public static void Reload()
        {
            Log.Info("==== pyACAD reload ====");
            try
            {
                RibbonProvider.Teardown();
                Panes.PaletteRegistry.Teardown();         // palettes rebuild with their toggles
                Execution.CommandRegistry.Unregister();   // the ribbon's targets go with it
                Input.ShortcutManager.UninstallHook(); // no hook may outlive its map
                Forms.Toast.CloseAll(); // a topmost window must never outlive the runtime that made it
                Forms.Banner.CloseAll();
                Output.PyAcadTheme.Invalidate(); // theme may have changed since boot
                Forms.FluentChrome.InvalidateAccent(); // and so may the Windows accent
                var userConfig = PyAcadConfig.Load(ConfigPath);
                _extensions = LoadExtensions(userConfig);

                var libsNow = LoadLibraries(userConfig);
                if (!libsNow.OrderBy(l => l).SequenceEqual(_libraries.OrderBy(l => l), StringComparer.OrdinalIgnoreCase))
                    Forms.Toast.Show("info", "Library extensions changed",
                        "Added or removed *.lib folders apply after an AutoCAD restart.");
                if (_libraries.Count > 0) EngineManager.InvalidateModules(_libraries);

                // Idempotent no-ops when Boot already wired them; they are what makes
                // Reload able to heal a Boot stage that failed.
                var steps = new StepRunner();
                _startupScriptsRan = false;   // every Reload re-runs startup.py
                RunStartupStages(steps, userConfig);
                steps.Run("App-init event", () => Events.AcadEvents.RaiseWhenIdle(Events.AcadEvent.AppInit));

                // Bundle-path modules invalidate per run; pyacadlib is a base search
                // path and stays cached, so Reload is where library edits get picked up.
                steps.Run("Library refresh", () =>
                {
                    var runtimeDir = Path.GetDirectoryName(typeof(RuntimeHost).Assembly.Location);
                    var pyacadlib = EngineConfigBuilder.ResolvePyAcadLib(runtimeDir, userConfig);
                    if (pyacadlib != null)
                        EngineManager.InvalidateModules(new[] { pyacadlib });
                });
                ReportFailures(steps, "Reload finished with problems");
            }
            catch (Exception ex)
            {
                Log.Error("Reload failed", ex);
                TryToast("Reload failed", "Details are in " + Log.LogDir);
            }
        }

        /// <summary>
        /// The stages Boot and Reload share, each failing alone: one bundle that throws
        /// while the ribbon builds must not also cost the user their hooks, context
        /// greying and keyboard shortcuts.
        /// </summary>
        private static void RunStartupStages(StepRunner steps, PyAcadConfig userConfig)
        {
            steps.Run("Event wiring", () =>
            {
                Events.AcadEvents.ObjectEventsEnabled = Events.AcadEventMap.WantsObjectEvents(_extensions);
                Events.AcadEvents.Wire();
            });
            // Configure before Build so button tooltips can show resolved chords.
            steps.Run("Shortcuts", () => Input.ShortcutManager.Configure(_extensions, userConfig));
            // Through EnsureStartupScriptsRan so Boot's deferred step is a no-op when
            // pyacad run already ran them; Reload clears the flag to run them again.
            steps.Run("Startup scripts", EnsureStartupScriptsRan);
            // Before Build, like Shortcuts above: the markers are read while captions and
            // icons are being made, so a config.json edit lands on the next Reload.
            // Before the ribbon, whose dock pane toggles look their palettes up by key.
            steps.Run("Dock panes", () => Panes.PaletteRegistry.Configure(_extensions));
            steps.Run("Ribbon markers", () => Ribbon.RibbonMarkers.Configure(userConfig));
            // Before the ribbon: a button sends its command, so the command must exist
            // by the time the first click can land.
            steps.Run("Commands", RegisterCommands);
            steps.Run("Ribbon", () =>
            {
                RibbonProvider.Build(_extensions);
                // Workspace switches and CUIx loads rebuild the ribbon from the
                // customization file, which has no pyACAD tabs: put them back.
                RibbonBootstrap.RebuildOnWorkspaceLoad(RibbonProvider.TabsPresent, () =>
                {
                    RibbonProvider.Teardown();
                    RibbonProvider.Build(_extensions);
                    Ribbon.ContextGate.Refresh();
                });
            });
            steps.Run("Hooks", () => Events.HookRunner.Configure(_extensions));
            steps.Run("Context rules", () =>
            {
                Ribbon.ContextGate.Wire();
                Ribbon.ContextGate.Refresh();
            });
            steps.Run("Keyboard shortcuts", Input.ShortcutManager.InstallHook);
        }

        /// <summary>
        /// One AutoCAD command per pushbutton bundle (nobuttons included: a command is
        /// their only way in besides a chord). Problems are the author's to see, so they
        /// toast like skipped bundles do.
        /// </summary>
        private static void RegisterCommands()
        {
            var buttons = _extensions.SelectMany(e => e.Tabs).SelectMany(t => t.Panels)
                .SelectMany(p => p.Buttons).ToList();
            var table = Execution.CommandNames.Assign(buttons);
            var problems = Execution.CommandRegistry.Register(table, assignment =>
            {
                if (assignment.IsConfig) Execution.ScriptExecutor.RunConfig(assignment.Model);
                else Execution.ScriptExecutor.Run(assignment.Model);
            });
            foreach (var problem in problems) Log.Error(problem);
            if (problems.Count > 0)
                Forms.Toast.Show("warning", problems.Count == 1 ? "One command was not registered"
                    : problems.Count + " commands were not registered", problems[0]);
        }

        /// <summary>
        /// What PYACAD and (pyacad ...) run: a .py file by path, or a bundle by its
        /// command name (with or without the PY prefix). The script sees the rest as
        /// __args__ and its __result__ comes back.
        /// </summary>
        public static Execution.ScriptOutcome Invoke(string nameOrPath, IList<object> args)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath))
                return Execution.ScriptOutcome.Refused("Give a script path or a command name.");
            var text = nameOrPath.Trim();
            if (text.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || File.Exists(text))
                return Execution.ScriptExecutor.RunFile(text, args);

            var table = Execution.CommandRegistry.Table;
            var assignment = table.Find(Execution.CommandNames.Sanitize(text))
                             ?? table.Find(Execution.CommandNames.Prefix + Execution.CommandNames.Sanitize(text));
            if (assignment == null)
                return Execution.ScriptOutcome.Refused("No script file or pyACAD command named '" + text + "'.");
            if (assignment.IsConfig)
            {
                Execution.ScriptExecutor.RunConfig(assignment.Model);
                return new Execution.ScriptOutcome { Succeeded = true };
            }
            return Execution.ScriptExecutor.Run(assignment.Model, args);
        }

        /// <summary>A failed stage used to be a log line only, so the user saw a missing
        /// ribbon or dead shortcuts and nothing else. Say it once, with the log folder.</summary>
        private static void ReportFailures(StepRunner steps, string headline)
        {
            var summary = steps.Summary();
            if (summary != null) TryToast(headline, summary);

            // "My button is not there" with nothing on screen to say why is the hardest
            // question a bundle author can be left with. The parser's own words answer it.
            var skipped = _extensions.SelectMany(e => e.Problems).ToList();
            if (skipped.Count == 0) return;
            try
            {
                Forms.Toast.Show("warning", ExtensionModel.ProblemsHeadline(skipped.Count),
                    skipped[0] + (skipped.Count > 1 ? " The rest are in " + Log.LogDir : ""));
            }
            catch (Exception ex)
            {
                Log.Error("Could not show the skipped-bundles toast", ex);
            }
        }

        private static void TryToast(string message, string detail)
        {
            try
            {
                Forms.Toast.Show("error", message, detail);
            }
            catch (Exception ex)
            {
                Log.Error("Could not show the failure toast", ex);
            }
        }

        /// <summary>Engine + environment diagnostic behind the loader's PYACADCONSOLE
        /// command: works even when no extension loaded, which is when it is needed.</summary>
        public static void RunConsoleFallback()
        {
            Log.Info("Console fallback invoked.");

            const string code = @"
import sys
import clr
clr.AddReference('System.Windows.Forms')
from System.Windows.Forms import MessageBox
MessageBox.Show(
    'pyACAD diagnostic\n\nIronPython %s\nAutoCAD API %s\nExtensions loaded: %s'
        % (sys.version, __api_version__, __extension_count__),
    'pyACAD')
";
            var request = new ScriptRequest { Code = code };
            request.Globals["__api_version__"] = ApiVersion;
            request.Globals["__extension_count__"] = _extensions.Count.ToString();

            var result = EngineManager.GetEngine().Execute(request);
            if (!result.Succeeded)
            {
                Log.Error("Diagnostic script failed:\n" + result.ErrorText);
                MessageBox.Show("pyACAD script error:\n\n" + result.ErrorText, "pyACAD",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static List<string> ExtensionRoots(PyAcadConfig userConfig)
        {
            return InstallPaths.ExtensionRoots(
                userConfig.ExtensionPaths, InstallPaths.DefaultExtensionRoots(),
                root => InstallPaths.UsableDefaultRoot(root, InstallPaths.MachineExtensionRoot,
                    Directory.Exists, AdminOnlyOrWarn));
        }

        private static bool AdminOnlyOrWarn(string root)
        {
            if (MachineFolderTrust.IsAdminOnly(root)) return true;
            Log.Error($"Extension root '{root}' skipped: users other than administrators can write to it, "
                     + "so code in it could come from anyone on this PC. Restrict it to administrators "
                     + "(read-only for Users), or list it under extensions in config.json to load it anyway.");
            return false;
        }

        private static List<ExtensionModel> LoadExtensions(PyAcadConfig userConfig)
        {
            var extensions = new List<ExtensionModel>();
            // Per root, so one that is offline or unreadable never costs the others.
            new StepRunner().Each(ExtensionRoots(userConfig), root => "Extension root '" + root + "'", root =>
            {
                var found = BundleParser.ParseRoot(root);
                Log.Info($"Extension root '{root}': {found.Count} extension(s).");
                extensions.AddRange(found);
            });
            return extensions;
        }

        private static List<string> LoadLibraries(PyAcadConfig userConfig)
        {
            var libs = new List<string>();
            new StepRunner().Each(ExtensionRoots(userConfig), root => "Library root '" + root + "'", root =>
            {
                var found = BundleParser.FindLibraries(root);
                if (found.Count > 0)
                    Log.Info($"Extension root '{root}': {found.Count} library extension(s).");
                libs.AddRange(found);
            });
            return libs;
        }

    }
}
