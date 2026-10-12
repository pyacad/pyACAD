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

        private static PushButtonModel Button(string title, string key, string command = null, bool hasConfig = false)
        {
            return new PushButtonModel
            {
                Title = title,
                BundleKey = key,
                ScriptPath = @"C:\x\" + key.Replace('/', '\\') + @"\script.py",
                ConfigScriptPath = hasConfig ? @"C:\x\config.py" : null,
                CommandName = command,
            };
        }
    }
}
