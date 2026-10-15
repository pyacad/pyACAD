using System.Collections.Generic;
using System.Linq;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Execution;
using Xunit;

namespace PyAcad.Tests
{
    public class CommandNamesTests
    {
        [Theory]
        [InlineData("Blocks to Excel", "PYBLOCKSTOEXCEL")]
        [InlineData("Reload", "PYRELOAD")]
        [InlineData("Ask AI (beta)", "PYASKAIBETA")]
        [InlineData("select-by ids", "PYSELECTBYIDS")]
        [InlineData("  spaced  ", "PYSPACED")]
        public void Derive_prefixes_and_strips_to_alphanumerics(string title, string expected)
        {
            Assert.Equal(expected, CommandNames.Derive(title));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("!!!")]
        [InlineData(null)]
        public void Derive_falls_back_when_nothing_usable_is_left(string title)
        {
            Assert.Equal("PYTOOL", CommandNames.Derive(title));
        }

        [Theory]
        [InlineData("myCmd", "MYCMD")]
        [InlineData("my cmd!", "MYCMD")]
        [InlineData("ab_cd-ef", "AB_CD-EF")]
        public void Sanitize_uppercases_and_keeps_only_legal_characters(string raw, string expected)
        {
            Assert.Equal(expected, CommandNames.Sanitize(raw));
        }

        [Fact]
        public void Sanitize_truncates_to_the_limit()
        {
            var longName = new string('A', 100);
            Assert.Equal(CommandNames.MaxLength, CommandNames.Sanitize(longName).Length);
        }

        [Fact]
        public void Assign_uses_the_override_when_present_and_derives_otherwise()
        {
            var a = Button("Alpha", key: "t/p/A.pushbutton");
            var b = Button("Beta", key: "t/p/B.pushbutton", command: "mybeta");
            var result = CommandNames.Assign(new[] { a, b });
            Assert.Equal("PYALPHA", result.Assignments.Single(x => x.Model == a).Name);
            Assert.Equal("MYBETA", result.Assignments.Single(x => x.Model == b).Name);
            Assert.Empty(result.Problems);
        }

        [Fact]
        public void Assign_is_first_wins_on_duplicates_and_reports_the_loser()
        {
            var a = Button("Same Name", key: "t/p/A.pushbutton");
            var b = Button("Same Name", key: "t/p/B.pushbutton");
            var result = CommandNames.Assign(new[] { a, b });
            Assert.Single(result.Assignments);
            Assert.Same(a, result.Assignments[0].Model);
            var problem = Assert.Single(result.Problems);
            Assert.Contains("PYSAMENAME", problem);
            Assert.Contains("t/p/B.pushbutton", problem);
        }

        [Fact]
        public void Assign_gives_a_config_script_its_own_CFG_command()
        {
            var a = Button("Alpha", key: "t/p/A.pushbutton", hasConfig: true);
            var result = CommandNames.Assign(new[] { a });
            Assert.Equal(2, result.Assignments.Count);
            var cfg = result.Assignments.Single(x => x.IsConfig);
            Assert.Equal("PYALPHACFG", cfg.Name);
            Assert.Same(a, cfg.Model);
        }

        [Fact]
        public void Assign_refuses_a_name_that_collides_with_a_builtin()
        {
            var a = Button("acad", key: "t/p/A.pushbutton", command: "PYACAD");
            var result = CommandNames.Assign(new[] { a });
            Assert.Empty(result.Assignments);
            Assert.Contains("PYACAD", Assert.Single(result.Problems));
        }

        [Fact]
        public void Lookup_finds_a_model_by_name_case_insensitively()
        {
            var a = Button("Alpha", key: "t/p/A.pushbutton");
            var result = CommandNames.Assign(new[] { a });
            Assert.Same(a, result.Find("pyalpha").Model);
            Assert.Null(result.Find("nothing"));
        }

        [Theory]
        [InlineData("bx", new[] { "BX" })]
        [InlineData("BX, B2X", new[] { "BX", "B2X" })]
        [InlineData(" bx  b2x;b3 ", new[] { "BX", "B2X", "B3" })]
        [InlineData("b.x, !!", new[] { "BX" })]
        [InlineData("", new string[0])]
        [InlineData(null, new string[0])]
        public void SplitAliases_splits_and_sanitises(string raw, string[] expected)
        {
            Assert.Equal(expected, CommandNames.SplitAliases(raw));
        }

        [Fact]
        public void Aliases_come_after_every_full_name_and_never_take_one()
        {
            var a = Button("Alpha", key: "t/p/A.pushbutton", aliases: "PYBETA, AX");
            var b = Button("Beta", key: "t/p/B.pushbutton");
            var table = CommandNames.Assign(new[] { a, b });

            Assert.Equal("PYBETA", table.NameFor(b));
            Assert.Same(b, table.Find("PYBETA").Model);
            Assert.Equal("PYALPHA", table.NameFor(a));
            Assert.Equal(new[] { "AX" }, table.AliasesFor(a).Select(x => x.Name));
            Assert.Same(a, table.Find("ax").Model);
            Assert.Contains("PYBETA", Assert.Single(table.Problems));
        }

        [Fact]
        public void An_alias_two_tools_claim_goes_to_the_first()
        {
            var a = Button("Alpha", key: "t/p/A.pushbutton", aliases: "XX");
            var b = Button("Beta", key: "t/p/B.pushbutton", aliases: "XX");
            var table = CommandNames.Assign(new[] { a, b });

            Assert.Same(a, table.Find("XX").Model);
            Assert.Empty(table.AliasesFor(b));
            Assert.Contains("bundle.yaml", Assert.Single(table.Problems));
        }

        [Fact]
        public void An_alias_equal_to_the_tools_own_command_is_dropped_quietly()
        {
            var a = Button("Alpha", key: "t/p/A.pushbutton", aliases: "pyalpha");
            var table = CommandNames.Assign(new[] { a });

            Assert.Empty(table.Problems);
            Assert.Empty(table.AliasesFor(a));
        }

        [Fact]
        public void Config_aliases_replace_or_remove_the_bundles()
        {
            var a = Button("Alpha", key: "t/p/A.pushbutton", aliases: "AX");
            var b = Button("Beta", key: "t/p/B.pushbutton", aliases: "BX");
            var c = Button("Gamma", key: "t/p/C.pushbutton", aliases: "GX");
            var overrides = new Dictionary<string, string>
            {
                ["t/p/A.pushbutton"] = "a1 a2",
                ["t/p/B.pushbutton"] = null,
            };
            var table = CommandNames.Assign(new[] { a, b, c }, overrides);

            Assert.Equal(new[] { "A1", "A2" }, table.AliasesFor(a).Select(x => x.Name));
            Assert.All(table.AliasesFor(a), x => Assert.True(x.FromUser));
            Assert.Null(table.Find("AX"));
            Assert.Empty(table.AliasesFor(b));
            Assert.False(Assert.Single(table.AliasesFor(c)).FromUser);
        }

        [Fact]
        public void A_users_alias_clash_points_at_config_json()
        {
            var a = Button("Alpha", key: "t/p/A.pushbutton");
            var b = Button("Beta", key: "t/p/B.pushbutton");
            var table = CommandNames.Assign(new[] { a, b }, new Dictionary<string, string> { ["t/p/B.pushbutton"] = "PYALPHA" });

            Assert.Contains("config.json", Assert.Single(table.Problems));
        }

        private static PushButtonModel Button(string title, string key, string command = null, bool hasConfig = false,
            string aliases = null)
        {
            var model = new PushButtonModel
            {
                Title = title,
                BundleKey = key,
                ScriptPath = @"C:\x\" + key.Replace('/', '\\') + @"\script.py",
                ConfigScriptPath = hasConfig ? @"C:\x\config.py" : null,
                CommandName = command,
            };
            model.Aliases.AddRange(CommandNames.SplitAliases(aliases));
            return model;
        }
    }
}
