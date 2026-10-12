using System;
using System.Collections.Generic;
using System.IO;
using PyAcad.Runtime.Config;
using Xunit;

namespace PyAcad.Tests
{
    public class PyAcadConfigTests : IDisposable
    {
        private readonly string _dir;

        public PyAcadConfigTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pyacad_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string WriteConfig(string json)
        {
            var p = Path.Combine(_dir, "config.json");
            File.WriteAllText(p, json);
            return p;
        }

        [Fact]
        public void MissingFile_YieldsDefaults()
        {
            var c = PyAcadConfig.Load(Path.Combine(_dir, "nope.json"));
            Assert.Empty(c.ExtensionPaths);
            Assert.False(c.ShortcutsAllowBareKeys);
            Assert.Empty(c.ShortcutBindings);
        }

        [Fact]
        public void Shortcuts_Section_ParsesBindings_NullsAndFlag()
        {
            var c = PyAcadConfig.Load(WriteConfig(@"{
                ""shortcuts"": {
                    ""allowBareKeys"": true,
                    ""bindings"": {
                        ""pyACAD.tab/Selection.panel/Recall.pushbutton"": ""Ctrl+Alt+R"",
                        ""pyACAD.tab/Selection.panel/Remember.pushbutton"": null
                    }
                }
            }"));

            Assert.True(c.ShortcutsAllowBareKeys);
            Assert.Equal("Ctrl+Alt+R", c.ShortcutBindings["pyACAD.tab/Selection.panel/Recall.pushbutton"]);
            Assert.True(c.ShortcutBindings.ContainsKey("pyACAD.tab/Selection.panel/Remember.pushbutton"));
            Assert.Null(c.ShortcutBindings["pyACAD.tab/Selection.panel/Remember.pushbutton"]);
        }

        // A config.json written before the shipped folders lost their NN_ prefixes names
        // tools by the old prefixed keys. They must land on the same tools after the
        // upgrade, so the installer needs no migration step of its own.
        [Fact]
        public void Load_DropsOrderingPrefixes_FromBindingAndPaneKeys()
        {
            var c = PyAcadConfig.Load(WriteConfig(@"{
                ""shortcuts"": { ""bindings"": {
                    ""pyACAD.tab/02_Selection.panel/02_Recall.pushbutton"": ""Ctrl+Alt+R"" } },
                ""panes"": { ""assignments"": {
                    ""pyACAD.tab/04_Layers.panel/05_Layer_Tracker.dockpane"": 2 } }
            }"));

            Assert.Equal("Ctrl+Alt+R", c.ShortcutBindings["pyACAD.tab/Selection.panel/Recall.pushbutton"]);
            Assert.False(c.ShortcutBindings.ContainsKey("pyACAD.tab/02_Selection.panel/02_Recall.pushbutton"));
            Assert.Equal(2, c.PaneAssignments["pyACAD.tab/Layers.panel/Layer_Tracker.dockpane"]);
        }

        [Fact]
        public void Reads_ExtensionsArray()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ \"extensions\": [\"D:\\\\a\", \"D:\\\\b\"] }"));
            Assert.Equal(new[] { @"D:\a", @"D:\b" }, c.ExtensionPaths);
        }

        [Fact]
        public void UnknownKeys_AreIgnored()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ \"runtime2026\": \"D:\\\\r\", \"extensions\": [\"D:\\\\a\"] }"));
            Assert.Single(c.ExtensionPaths);
        }

        [Fact]
        public void InvalidJson_YieldsDefaults_DoesNotThrow()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ not valid json"));
            Assert.Empty(c.ExtensionPaths);
        }

        [Fact]
        public void MissingExtensionsKey_YieldsEmptyList()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ \"runtime2026\": \"D:\\\\r\" }"));
            Assert.Empty(c.ExtensionPaths);
        }

        [Fact]
        public void Reads_PyAcadLibPath()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ \"pyacadlib\": \"D:\\\\repo\\\\pyacadlib\" }"));
            Assert.Equal(@"D:\repo\pyacadlib", c.PyAcadLibPath);
        }

        [Fact]
        public void MissingPyAcadLibKey_YieldsNull()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ \"extensions\": [\"D:\\\\a\"] }"));
            Assert.Null(c.PyAcadLibPath);
        }

        [Fact]
        public void NonStringPyAcadLibKey_YieldsNull()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ \"pyacadlib\": [\"D:\\\\a\"] }"));
            Assert.Null(c.PyAcadLibPath);
        }

        [Fact]
        public void Reads_CPythonKey()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ \"cpython\": \"C:\\\\Py311\\\\python311.dll\" }"));
            Assert.Equal(@"C:\Py311\python311.dll", c.CPythonPath);
        }

        [Fact]
        public void MissingCPythonKey_YieldsNull()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ }"));
            Assert.Null(c.CPythonPath);
        }

        [Fact]
        public void Reads_ThemeKey()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ \"theme\": \"dark\" }"));
            Assert.Equal("dark", c.Theme);
        }

        [Theory]
        [InlineData("{ \"automatedBoot\": \"full\" }", true)]
        [InlineData("{ \"automatedBoot\": \"Full\" }", true)]
        [InlineData("{ \"automatedBoot\": \"skip\" }", false)]
        [InlineData("{ }", false)]
        public void Reads_AutomatedBootKey(string json, bool expected)
        {
            var c = PyAcadConfig.Load(WriteConfig(json));
            Assert.Equal(expected, c.AutomatedBootFull);
        }

        [Fact]
        public void MissingThemeKey_YieldsNull()
        {
            var c = PyAcadConfig.Load(WriteConfig("{ }"));
            Assert.Null(c.Theme);
        }

        // ---- SaveShortcutBindings: surgical writes, everything else untouched ----

        private static Dictionary<string, string> Bindings(params (string key, string chord)[] pairs)
        {
            var map = new Dictionary<string, string>();
            foreach (var (key, chord) in pairs) map[key] = chord;
            return map;
        }

        [Fact]
        public void Save_PreservesUnknownKeys_AndReplacesOnlyBindings()
        {
            var path = WriteConfig(@"{
                ""runtime2026"": ""D:\\r"",
                ""extensions"": [""D:\\a"", ""D:\\b""],
                ""theme"": ""dark"",
                ""shortcuts"": { ""allowBareKeys"": true, ""bindings"": { ""old/key"": ""Ctrl+Alt+O"" } }
            }");

            PyAcadConfig.SaveShortcutBindings(path, Bindings(("new/key", "Ctrl+Alt+N")));

            var c = PyAcadConfig.Load(path);
            Assert.Equal(new[] { @"D:\a", @"D:\b" }, c.ExtensionPaths);
            Assert.Equal("dark", c.Theme);
            Assert.True(c.ShortcutsAllowBareKeys);
            Assert.Equal("Ctrl+Alt+N", c.ShortcutBindings["new/key"]);
            Assert.False(c.ShortcutBindings.ContainsKey("old/key"));
            Assert.Contains("\"runtime2026\"", File.ReadAllText(path)); // unknown key survives verbatim
        }

        [Fact]
        public void SaveLayout_RoundTrips_AndLeavesOtherDialogsAlone()
        {
            var path = WriteConfig(@"{
                ""theme"": ""dark"",
                ""layout"": { ""other"": { ""tree"": 99 } }
            }");

            var parts = new Dictionary<string, double> { ["tree"] = 240.6, ["panel"] = 300, ["gone"] = 0 };
            PyAcadConfig.SaveLayout(path, "layers", parts);

            var c = PyAcadConfig.Load(path);
            Assert.Equal("dark", c.Theme);
            Assert.Equal(241, c.Layout["layers/tree"]);
            Assert.Equal(300, c.Layout["layers/panel"]);
            Assert.False(c.Layout.ContainsKey("layers/gone"));
            Assert.Equal(99, c.Layout["other/tree"]);
        }

        [Fact]
        public void Load_Layout_IgnoresJunkValues()
        {
            var path = WriteConfig(@"{ ""layout"": { ""layers"": { ""tree"": ""wide"", ""panel"": -5, ""ok"": 12.5 } } }");

            var c = PyAcadConfig.Load(path);
            Assert.Single(c.Layout);
            Assert.Equal(12.5, c.Layout["layers/ok"]);
        }

        [Fact]
        public void Save_NullValue_RoundTripsAsDisable()
        {
            var path = WriteConfig("{ }");

            PyAcadConfig.SaveShortcutBindings(path, Bindings(("some/key", null)));

            var c = PyAcadConfig.Load(path);
            Assert.True(c.ShortcutBindings.ContainsKey("some/key"));
            Assert.Null(c.ShortcutBindings["some/key"]);
        }

        [Fact]
        public void Save_MissingFile_CreatesDirAndMinimalConfig()
        {
            var path = Path.Combine(_dir, "sub", "config.json");

            PyAcadConfig.SaveShortcutBindings(path, Bindings(("some/key", "Ctrl+Alt+K")));

            var c = PyAcadConfig.Load(path);
            Assert.Equal("Ctrl+Alt+K", c.ShortcutBindings["some/key"]);
        }

        [Fact]
        public void Save_EmptyMap_RemovesBindings_AndEmptyShortcutsSection()
        {
            var path = WriteConfig(@"{ ""shortcuts"": { ""bindings"": { ""k"": ""Ctrl+Alt+K"" } }, ""theme"": ""dark"" }");

            PyAcadConfig.SaveShortcutBindings(path, Bindings());

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("shortcuts", text);
            Assert.Equal("dark", PyAcadConfig.Load(path).Theme);
        }

        [Fact]
        public void Save_EmptyMap_KeepsShortcutsSection_WhenAllowBareKeysSet()
        {
            var path = WriteConfig(@"{ ""shortcuts"": { ""allowBareKeys"": true, ""bindings"": { ""k"": ""F5"" } } }");

            PyAcadConfig.SaveShortcutBindings(path, Bindings());

            var c = PyAcadConfig.Load(path);
            Assert.True(c.ShortcutsAllowBareKeys);
            Assert.Empty(c.ShortcutBindings);
        }

        // A save over a file we cannot parse used to start from an empty root, which
        // dropped the extension paths, the for-life pane slots and the runtimeNNNN key
        // the loader boots from. One stray comma must never cost the user all of that.
        [Fact]
        public void SaveShortcutBindings_UnparsableExistingFile_Refuses_AndLeavesFileUntouched()
        {
            var broken = "{ \"runtime2026\": \"C:/rt\", not valid json";
            var path = WriteConfig(broken);

            Assert.Throws<InvalidDataException>(() =>
                PyAcadConfig.SaveShortcutBindings(path, Bindings(("some/key", "Ctrl+Alt+K"))));

            Assert.Equal(broken, File.ReadAllText(path));
        }

        [Fact]
        public void SaveLayout_UnparsableExistingFile_Refuses_AndLeavesFileUntouched()
        {
            var broken = "{ \"runtime2026\": \"C:/rt\", not valid json";
            var path = WriteConfig(broken);

            Assert.Throws<InvalidDataException>(() =>
                PyAcadConfig.SaveLayout(path, "layers", new Dictionary<string, double> { ["tree"] = 200 }));

            Assert.Equal(broken, File.ReadAllText(path));
        }

        [Fact]
        public void SavePaneAssignments_UnparsableExistingFile_Refuses_AndLeavesFileUntouched()
        {
            var broken = "{ \"runtime2026\": \"C:/rt\", not valid json";
            var path = WriteConfig(broken);

            Assert.Throws<InvalidDataException>(() =>
                PyAcadConfig.SavePaneAssignments(path, new Dictionary<string, int> { ["a/b"] = 1 }, 0));

            Assert.Equal(broken, File.ReadAllText(path));
        }

        // A zero-byte file is what a crash mid-write leaves behind: nothing to preserve,
        // so saving over it is safe and is the way back to a working config.
        [Fact]
        public void Save_EmptyExistingFile_StartsFresh()
        {
            var path = WriteConfig("");

            PyAcadConfig.SaveShortcutBindings(path, Bindings(("some/key", "Ctrl+Alt+K")));

            Assert.Equal("Ctrl+Alt+K", PyAcadConfig.Load(path).ShortcutBindings["some/key"]);
        }

        // A locked file (another AutoCAD session mid-save, antivirus) is not
        // "unreadable - start fresh": the save must fail and the content must survive.
        [Fact]
        public void Save_LockedExistingFile_Throws_AndLeavesFileUntouched()
        {
            var original = "{ \"runtime2026\": \"C:/rt\", \"extensions\": [\"C:/ext\"] }";
            var path = WriteConfig(original);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() =>
                    PyAcadConfig.SaveLayout(path, "layers", new Dictionary<string, double> { ["tree"] = 200 }));
            }

            Assert.Equal(original, File.ReadAllText(path));
        }

        [Fact]
        public void Save_LeavesNoTempFileBehind()
        {
            var path = WriteConfig("{ \"theme\": \"dark\" }");

            PyAcadConfig.SaveShortcutBindings(path, Bindings(("some/key", "Ctrl+Alt+K")));

            Assert.Equal(new[] { path }, Directory.GetFiles(_dir));
        }
    }
}
