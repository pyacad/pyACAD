using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Config;
using PyAcad.Runtime.Forms;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// The alias column of the shortcuts editor: author defaults merged with config.json,
    /// edits canonicalised, clashes flagged in CommandNames' order (full names first, then
    /// aliases in ribbon order), and only the user's own clashes blocking Save.
    /// </summary>
    public class ShortcutsModelAliasTests
    {
        private static ExtensionModel Ext(params (string key, string title, string aliases)[] buttons)
        {
            var ext = new ExtensionModel { Name = "T", Directory = @"C:\x" };
            var tab = new TabModel { Title = "T", Id = "T" };
            ext.Tabs.Add(tab);
            var panel = new PanelModel { Title = "General" };
            foreach (var b in buttons)
            {
                var model = new PushButtonModel { BundleKey = b.key, Title = b.title, ScriptPath = b.key };
                model.Aliases.AddRange(Runtime.Execution.CommandNames.SplitAliases(b.aliases));
                panel.Items.Add(model);
            }
            tab.Panels.Add(panel);
            return ext;
        }

        private static PyAcadConfig Config(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), "pyacad-test-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, json);
            try { return PyAcadConfig.Load(path); }
            finally { File.Delete(path); }
        }

        private static ShortcutsModel Model(PyAcadConfig config = null, Func<string, string> outside = null) =>
            ShortcutsModel.Build(new[]
            {
                Ext(("t/g/A.pushbutton", "Alpha", "AX"),
                    ("t/g/B.pushbutton", "Beta", null),
                    ("t/g/C.pushbutton", "Gamma", "gx, g2")),
            }, config ?? PyAcadConfig.Load(null), outside);

        private static ShortcutsModel.Row Row(ShortcutsModel m, string key) => m.Rows.Single(r => r.BundleKey == key);

        [Fact]
        public void Rows_Carry_The_Command_And_The_Authors_Aliases()
        {
            var m = Model();

            Assert.Equal("PYALPHA", Row(m, "t/g/A.pushbutton").CommandName);
            Assert.Equal("AX", Row(m, "t/g/A.pushbutton").CurrentAliases);
            Assert.Null(Row(m, "t/g/B.pushbutton").CurrentAliases);
            Assert.Equal("GX, G2", Row(m, "t/g/C.pushbutton").DefaultAliases);
            Assert.False(m.IsDirty);
            Assert.Empty(m.ToAliases());
        }

        [Fact]
        public void Config_Aliases_Replace_Or_Drop_The_Authors()
        {
            var m = Model(Config(@"{ ""aliases"": { ""t/g/A.pushbutton"": ""a1 a2"", ""t/g/C.pushbutton"": null } }"));

            Assert.Equal("A1, A2", Row(m, "t/g/A.pushbutton").CurrentAliases);
            Assert.True(Row(m, "t/g/A.pushbutton").IsAliasCustom);
            Assert.Null(Row(m, "t/g/C.pushbutton").CurrentAliases);
            Assert.False(m.IsDirty);
        }

        [Fact]
        public void Typing_Canonicalises_And_Makes_The_Model_Dirty()
        {
            var m = Model();

            Assert.Null(m.TrySetAliases("t/g/B.pushbutton", " bx;b2x  bx "));
            Assert.Equal("BX, B2X", Row(m, "t/g/B.pushbutton").CurrentAliases);
            Assert.True(m.IsDirty);
            Assert.Equal("BX, B2X", m.ToAliases()["t/g/B.pushbutton"]);

            Assert.Null(m.TrySetAliases("t/g/B.pushbutton", ""));
            Assert.False(m.IsDirty);

            Assert.NotNull(m.TrySetAliases("t/g/B.pushbutton", "!!"));
            Assert.Equal("unknown tool", m.TrySetAliases("nope", "X"));
        }

        [Fact]
        public void Clearing_An_Authors_Alias_Saves_As_Null()
        {
            var m = Model();
            m.TrySetAliases("t/g/A.pushbutton", "");

            var saved = m.ToAliases();
            Assert.True(saved.ContainsKey("t/g/A.pushbutton"));
            Assert.Null(saved["t/g/A.pushbutton"]);
        }

        [Fact]
        public void An_Alias_Another_Tool_Holds_Blocks_Save_With_Its_Name()
        {
            var m = Model();
            m.TrySetAliases("t/g/B.pushbutton", "AX");

            var beta = Row(m, "t/g/B.pushbutton");
            Assert.Equal("AX is taken by Alpha", beta.AliasProblem);
            Assert.True(beta.AliasBlocks);
            Assert.True(m.HasAliasProblems);
        }

        [Fact]
        public void An_Alias_May_Never_Take_A_Full_Command_Name()
        {
            var m = Model();
            m.TrySetAliases("t/g/A.pushbutton", "PYBETA");

            Assert.Equal("PYBETA is taken by Beta", Row(m, "t/g/A.pushbutton").AliasProblem);
        }

        [Fact]
        public void An_Alias_Equal_To_Its_Own_Command_Is_No_Problem()
        {
            var m = Model();
            m.TrySetAliases("t/g/A.pushbutton", "PYALPHA");

            Assert.Null(Row(m, "t/g/A.pushbutton").AliasProblem);
        }

        [Fact]
        public void A_Name_Outside_PyAcad_Owns_Blocks_A_Users_Alias_But_Only_Notes_An_Authors()
        {
            Func<string, string> outside = n => n == "CO" ? "in acad.pgp" : n == "GX" ? "an AutoCAD command" : null;
            var m = Model(outside: outside);

            var gamma = Row(m, "t/g/C.pushbutton");
            Assert.Equal("GX is an AutoCAD command, which keeps it", gamma.AliasProblem);
            Assert.False(gamma.AliasBlocks);   // the author's: registration skips it, nothing to fix
            Assert.False(m.HasAliasProblems);

            m.TrySetAliases("t/g/B.pushbutton", "CO");
            Assert.Equal("CO is in acad.pgp, which keeps it", Row(m, "t/g/B.pushbutton").AliasProblem);
            Assert.True(m.HasAliasProblems);
        }

        [Fact]
        public void Reset_Puts_Chord_And_Aliases_Back()
        {
            var m = Model();
            m.TrySetAliases("t/g/A.pushbutton", "ZZ");
            m.TrySetAliases("t/g/C.pushbutton", "");

            m.ResetToDefault("t/g/A.pushbutton");
            Assert.Equal("AX", Row(m, "t/g/A.pushbutton").CurrentAliases);
            Assert.True(m.IsDirty);

            m.ResetAll();
            Assert.Equal("GX, G2", Row(m, "t/g/C.pushbutton").CurrentAliases);
            Assert.False(m.IsDirty);
        }

        [Fact]
        public void BundleKeys_Lists_Every_Row()
        {
            Assert.Equal(new[] { "t/g/A.pushbutton", "t/g/B.pushbutton", "t/g/C.pushbutton" }, Model().BundleKeys);
        }
    }
}
