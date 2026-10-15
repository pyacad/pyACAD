using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PyAcad.Runtime.Bundles;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// The repo's shipped pyACAD.extension, parsed with the real BundleParser:
    /// catches broken bundle folders (missing script.py, renamed panels) at test time
    /// instead of as silently missing ribbon buttons.
    /// </summary>
    public class ShippedExtensionTests
    {
        private static ExtensionModel ParseShipped()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "extensions", "pyACAD.extension");
                if (Directory.Exists(candidate)) return BundleParser.ParseExtension(candidate);
            }
            throw new DirectoryNotFoundException("shipped pyACAD.extension not found above test dir");
        }

        [Fact]
        public void ThePanels_ReadInLayoutOrder()
        {
            // The tab's layout list orders the panels: setup first, the MEP tool panels, then AI.
            // Name order would put AI first, so this only holds while the list does.
            var tab = Assert.Single(ParseShipped().Tabs);
            Assert.Equal(new[] { "pyACAD", "Data", "Layers", "Xrefs", "Cleanup", "Output", "Takeoff", "AI (beta)" },
                tab.Panels.Select(p => p.Title).ToArray());
        }

        [Fact]
        public void ShippedCommandsAndAliases_AllGetTheirNames()
        {
            // Every shipped tool keeps its command, and every shipped alias is unique and
            // never one of the tools' full names. Clashes with acad.pgp are a runtime check.
            var buttons = ParseShipped().Tabs.SelectMany(t => t.Panels).SelectMany(p => p.Buttons).ToList();
            var table = Runtime.Execution.CommandNames.Assign(buttons);

            Assert.Empty(table.Problems);
            Assert.Equal(14, table.Assignments.Count(a => a.IsAlias));
            var bx = table.Find("BX");
            Assert.NotNull(bx);
            Assert.Equal("PYBLOCKSTOEXCEL", table.NameFor(bx.Model));
        }

        [Fact]
        public void ShippedExtension_ParsesWithoutProblems()
        {
            // A layout entry that names no folder, a mistyped suffix, a button without a
            // script: all land here, and none may ship.
            Assert.Empty(ParseShipped().Problems);
        }

        // Folders carry no ordering prefixes any more, so the ribbon order is whatever the
        // layout lists say. Every container must have one and it must name every child,
        // or a new tool silently lands at the end of its panel in name order.
        [Fact]
        public void EveryShippedContainer_ListsEveryChild_InItsLayout()
        {
            var suffixes = new[] { ".tab", ".panel", ".stack", ".pulldown", ".slideout", ".splitbutton", ".splitpushbutton" };
            var containers = Directory.GetDirectories(ShippedExtensionDir(), "*", SearchOption.AllDirectories)
                .Where(d => suffixes.Any(s => d.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            Assert.NotEmpty(containers);

            foreach (var dir in containers)
            {
                var yamlPath = Path.Combine(dir, "bundle.yaml");
                Assert.True(File.Exists(yamlPath), $"{dir} has no bundle.yaml with a layout");
                var listed = BundleYaml.ListOf(BundleYaml.Parse(File.ReadAllText(yamlPath)), "layout")
                    .Select(BareName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                var children = Directory.GetDirectories(dir).Select(Path.GetFileName)
                    .Where(n => n.IndexOf('.') > 0)
                    .Select(BareName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                Assert.True(children.SequenceEqual(listed, StringComparer.OrdinalIgnoreCase),
                    $"{dir}: layout lists [{string.Join(", ", listed)}] but the folders are [{string.Join(", ", children)}]");
            }
        }

        [Fact]
        public void NoShippedFolder_CarriesAnOrderingPrefix()
        {
            var prefixed = Directory.GetDirectories(ShippedExtensionDir(), "*", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^\d{2,}_"))
                .ToArray();
            Assert.Empty(prefixed);
        }

        private static string BareName(string folderOrEntry)
        {
            var dot = folderOrEntry.LastIndexOf('.');
            return dot > 0 ? folderOrEntry.Substring(0, dot) : folderOrEntry;
        }

        [Fact]
        public void AiPanel_ShipsTheChatPane_AndASetupStack_AllMarkedBeta()
        {
            var ai = ParseShipped().Tabs.Single().Panels.Single(p => p.Title == "AI (beta)");

            Assert.Collection(ai.Items,
                i =>
                {
                    var pane = Assert.IsType<DockPaneModel>(i);
                    Assert.Equal("Ask AI", pane.Title);
                    Assert.Contains("beta", pane.Tooltip, StringComparison.OrdinalIgnoreCase);
                    Assert.True(File.Exists(pane.XamlPath), "missing pane.xaml");
                    Assert.NotNull(pane.ScriptPath);
                    foreach (var name in new[] { "icon.on.png", "icon.on.dark.png" })
                        Assert.True(File.Exists(Path.Combine(pane.Directory, name)), "missing " + name);
                },
                i => Assert.Equal(new[] { "AI Settings", "Open AI Folder" },
                        Assert.IsType<StackModel>(i).Buttons.Select(b => b.Title).ToArray()));
            Assert.All(ai.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.Tooltip)));
            Assert.Empty(ai.Slideout);
        }

        [Fact]
        public void PyAcadPanel_Ships_Settings_Shortcuts_Console_Reload()
        {
            var tab = Assert.Single(ParseShipped().Tabs);
            var general = tab.Panels.Single(p => p.Title == "pyACAD");

            // Settings first, the developer tools last: a coordinator should not
            // meet "Console" before anything else.
            Assert.Equal(new[] { "Settings", "Shortcuts", "Console", "Reload" },
                general.Items.OfType<PushButtonModel>().Select(b => b.Title).ToArray());
            Assert.Empty(general.Slideout);
        }

        [Fact]
        public void ShippedExtension_ShipsNoHooks()
        {
            // The one shipped hook put back tabs that Hide Tabs took off; both are gone.
            Assert.Empty(ParseShipped().Hooks);
        }

        /// <summary>PNG width/height straight out of the IHDR header, so the test needs
        /// no imaging dependency.</summary>
        private static (int width, int height) PngSize(string path)
        {
            var head = new byte[24];
            using (var stream = File.OpenRead(path))
            {
                if (stream.Read(head, 0, head.Length) < head.Length)
                    throw new InvalidDataException(path + " is too short to be a PNG");
            }
            int Be(int offset) => (head[offset] << 24) | (head[offset + 1] << 16)
                                | (head[offset + 2] << 8) | head[offset + 3];
            return (Be(16), Be(20));
        }

        [Fact]
        public void EveryShippedBundle_HasAllFourIconVariants_AtTheRightSizes()
        {
            var bundles = ParseShipped().Tabs
                .SelectMany(t => t.Panels)
                .SelectMany(p => p.Items.Concat(p.Slideout))
                .Where(i => !(i is PushButtonModel hidden && hidden.NoUi))   // chord-only, never drawn
                .SelectMany(i =>
                    i is PulldownModel pulldown
                        ? new[] { pulldown.Directory }.Concat(pulldown.Buttons.Select(b => b.Directory))
                        : i is StackModel stack
                            ? stack.Buttons.Select(b => b.Directory)
                            : i is DockPaneModel pane
                                ? new[] { pane.Directory }
                                : new[] { ((PushButtonModel)i).Directory })
                .ToList();

            Assert.Equal(22, bundles.Count);

            var expected = new[]
            {
                ("icon.png", 96), ("icon.dark.png", 96),
                ("icon.small.png", 32), ("icon.small.dark.png", 32),
            };
            // A toggle draws its large art per state instead of one icon.png.
            var toggle = new[]
            {
                ("icon.off.png", 96), ("icon.off.dark.png", 96),
                ("icon.on.png", 96), ("icon.on.dark.png", 96),
                ("icon.small.png", 32), ("icon.small.dark.png", 32),
            };
            var toggles = new HashSet<string>(ParseShipped().Tabs
                .SelectMany(t => t.Panels).SelectMany(p => p.Buttons)
                .Where(b => b.IsToggle).Select(b => b.Directory));

            foreach (var dir in bundles)
            {
                foreach (var (name, size) in toggles.Contains(dir) ? toggle : expected)
                {
                    var path = Path.Combine(dir, name);
                    Assert.True(File.Exists(path), $"missing {name} in {dir}");

                    var (width, height) = PngSize(path);
                    Assert.True(width == size && height == size,
                        $"{path} is {width}x{height}, expected {size}x{size}");
                }
            }
        }

        private static string ShippedExtensionDir()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "extensions", "pyACAD.extension");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("shipped pyACAD.extension not found above test dir");
        }

        [Fact]
        public void AllShippedScripts_Compile()
        {
            var engine = new Runtime.Engine.IronPythonEngine();
            var config = new Runtime.Engine.EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            engine.Initialize(config);

            var model = ParseShipped();
            var scripts = model.Tabs
                .SelectMany(t => t.Panels)
                .SelectMany(p => p.Buttons)
                .SelectMany(b => new[] { b.ScriptPath, b.ConfigScriptPath })
                .Concat(model.Tabs
                    .SelectMany(t => t.Panels)
                    .SelectMany(p => p.Items.Concat(p.Slideout))
                    .OfType<DockPaneModel>()
                    .Select(d => d.ScriptPath))
                .Where(p => p != null)
                .ToList();

            // Any config.py (a Shift+Click secondary action) is compiled alongside its
            // script.py by the SelectMany above.
            // A dockpane's script.py hangs off no PushButtonModel, so without the
            // Concat above it would ship uncompiled and break only at pane build.
            Assert.Contains(scripts, s => s.Contains(".dockpane"));

            foreach (var path in scripts)
            {
                var req = new Runtime.Engine.ScriptRequest
                {
                    Code = "with open(__source_path__) as f:\n"
                         + "    compile(f.read(), __source_path__, 'exec')",
                };
                req.Globals["__source_path__"] = path;

                var r = engine.Execute(req);
                Assert.True(r.Succeeded, $"{path} failed to compile:\n{r.ErrorText}");
            }
        }

    }
}
