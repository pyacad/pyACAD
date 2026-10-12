using System.Collections.Generic;
using PyAcad.Runtime.Host;
using Xunit;

namespace PyAcad.Tests
{
    public class AcadInstallTests
    {
        private static AcadInstall.IRegistry Fake(Dictionary<string, Dictionary<string, string>> tree)
            => new FakeRegistry(tree);

        [Fact]
        public void Finds_the_install_folder_under_the_series_key()
        {
            var reg = Fake(new Dictionary<string, Dictionary<string, string>>
            {
                [@"SOFTWARE\Autodesk\AutoCAD\R25.1"] = new Dictionary<string, string> { ["CurVer"] = "ACAD-8101:409" },
                [@"SOFTWARE\Autodesk\AutoCAD\R25.1\ACAD-8101:409"] = new Dictionary<string, string>
                {
                    ["AcadLocation"] = @"D:\Program Files\Autodesk\AutoCAD 2026",
                },
            });
            Assert.Equal(@"D:\Program Files\Autodesk\AutoCAD 2026", AcadInstall.FindInstallDir(2026, reg));
        }

        [Fact]
        public void Prefers_plain_AutoCAD_over_a_vertical_when_both_exist()
        {
            var reg = Fake(new Dictionary<string, Dictionary<string, string>>
            {
                [@"SOFTWARE\Autodesk\AutoCAD\R24.3"] = new Dictionary<string, string>(),
                [@"SOFTWARE\Autodesk\AutoCAD\R24.3\ACAD-7001:409"] = new Dictionary<string, string>
                {
                    ["AcadLocation"] = @"C:\MEP 2024", ["ProductName"] = "AutoCAD MEP 2024",
                },
                [@"SOFTWARE\Autodesk\AutoCAD\R24.3\ACAD-7101:409"] = new Dictionary<string, string>
                {
                    ["AcadLocation"] = @"C:\ACAD 2024", ["ProductName"] = "AutoCAD 2024",
                },
            });
            Assert.Equal(@"C:\ACAD 2024", AcadInstall.FindInstallDir(2024, reg));
        }

        [Fact]
        public void Returns_null_when_the_series_is_not_installed()
        {
            var reg = Fake(new Dictionary<string, Dictionary<string, string>>());
            Assert.Null(AcadInstall.FindInstallDir(2023, reg));
        }

        [Fact]
        public void Returns_null_for_an_unsupported_year()
        {
            var reg = Fake(new Dictionary<string, Dictionary<string, string>>());
            Assert.Null(AcadInstall.FindInstallDir(2019, reg));
        }

        [Fact]
        public void Lists_every_installed_supported_year()
        {
            var reg = Fake(new Dictionary<string, Dictionary<string, string>>
            {
                [@"SOFTWARE\Autodesk\AutoCAD\R24.2\ACAD-6001:409"] = new Dictionary<string, string> { ["AcadLocation"] = @"C:\A23" },
                [@"SOFTWARE\Autodesk\AutoCAD\R25.1\ACAD-8101:409"] = new Dictionary<string, string> { ["AcadLocation"] = @"C:\A26" },
                [@"SOFTWARE\Autodesk\AutoCAD\R19.1\ACAD-D001:409"] = new Dictionary<string, string> { ["AcadLocation"] = @"C:\A14" },
            });
            Assert.Equal(new[] { 2023, 2026 }, AcadInstall.InstalledYears(reg));
        }

        [Theory]
        [InlineData(2021, "R24.0")]
        [InlineData(2026, "R25.1")]
        public void Series_matches_the_bundle_layout_table(int year, string series)
        {
            Assert.Equal(series, AcadInstall.SeriesOf(year));
        }

        private sealed class FakeRegistry : AcadInstall.IRegistry
        {
            private readonly Dictionary<string, Dictionary<string, string>> _tree;
            public FakeRegistry(Dictionary<string, Dictionary<string, string>> tree) { _tree = tree; }

            public IEnumerable<string> SubKeys(string path)
            {
                var prefix = path + "\\";
                foreach (var key in _tree.Keys)
                    if (key.StartsWith(prefix) && key.IndexOf('\\', prefix.Length) < 0)
                        yield return key.Substring(prefix.Length);
            }

            public string Value(string path, string name) =>
                _tree.TryGetValue(path, out var values) && values.TryGetValue(name, out var v) ? v : null;
        }
    }
}
