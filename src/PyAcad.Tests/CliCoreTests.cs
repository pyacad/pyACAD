using System;
using System.IO;
using PyAcad.Cli;
using PyAcad.Runtime.Config;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// The pyacad CLI's core operations, exercised against fake filesystem layouts.
    /// The exe's Main is a thin arg-parser over this class.
    /// </summary>
    public class CliCoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _binRoot;
        private readonly string _acadDir;
        private readonly string _bundleRoot;
        private readonly string _configPath;
        private readonly StringWriter _out = new StringWriter();
        private readonly CliCore _cli;

        public CliCoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pyacad_tests", Guid.NewGuid().ToString("N"));
            _binRoot = Path.Combine(_dir, "bin");
            _acadDir = Path.Combine(_dir, "AutoCAD 2026");
            _bundleRoot = Path.Combine(_dir, "ApplicationPlugins", "pyACAD.bundle");
            _configPath = Path.Combine(_dir, "config.json");

            Directory.CreateDirectory(Path.Combine(_binRoot, "2026"));
            File.WriteAllText(Path.Combine(_binRoot, "2026", "PyAcad.dll"), "fake loader");
            Directory.CreateDirectory(_acadDir);
            File.WriteAllText(Path.Combine(_acadDir, "acad.exe"), "");

            _cli = new CliCore(_binRoot, _configPath, _bundleRoot, year => year == "2026" ? _acadDir : null, _out);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Attach_CopiesLoader_IntoTheBundle_AndRegistersItsYear()
        {
            var exit = _cli.Attach("2026");

            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(_bundleRoot, "Contents", "2026", "PyAcad.dll")));
            var manifest = File.ReadAllText(Path.Combine(_bundleRoot, "PackageContents.xml"));
            Assert.Contains("SeriesMin=\"R25.1\"", manifest);
            Assert.Contains("ModuleName=\"./Contents/2026/PyAcad.dll\"", manifest);
            Assert.Contains("AppType=\".Net\"", manifest);
        }

        [Fact]
        public void Attach_Twice_KeepsOneEntry_AndOtherYearsIntact()
        {
            Directory.CreateDirectory(Path.Combine(_bundleRoot, "Contents", "2025"));
            Directory.CreateDirectory(_bundleRoot);
            File.WriteAllText(Path.Combine(_bundleRoot, "PackageContents.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><ApplicationPackage SchemaVersion=\"1.0\" Name=\"pyACAD\" AppVersion=\"0.4.0\">" +
                "<Components Description=\"AutoCAD 2025\"><RuntimeRequirements OS=\"Win64\" Platform=\"AutoCAD*\" SeriesMin=\"R25.0\" SeriesMax=\"R25.0\" />" +
                "<ComponentEntry AppName=\"pyACAD\" AppType=\".Net\" ModuleName=\"./Contents/2025/PyAcad.dll\" /></Components></ApplicationPackage>");

            _cli.Attach("2026");
            _cli.Attach("2026");

            var manifest = File.ReadAllText(Path.Combine(_bundleRoot, "PackageContents.xml"));
            Assert.Equal(1, CountOf(manifest, "./Contents/2026/PyAcad.dll"));
            Assert.Equal(1, CountOf(manifest, "./Contents/2025/PyAcad.dll"));
        }

        [Theory]
        [InlineData("2021", "R24.0")]
        [InlineData("2024", "R24.3")]
        [InlineData("2025", "R25.0")]
        [InlineData("2026", "R25.1")]
        [InlineData("2027", null)]
        public void SeriesOf_MapsEachSupportedYear(string year, string series) =>
            Assert.Equal(series, CliCore.SeriesOf(year));

        private static int CountOf(string text, string needle)
        {
            int count = 0, i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
            return count;
        }

        [Fact]
        public void Attach_UnknownVersion_Fails()
        {
            Assert.Equal(1, _cli.Attach("2024"));
            Assert.Contains("not found", _out.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Attach_WithoutBuild_Fails()
        {
            File.Delete(Path.Combine(_binRoot, "2026", "PyAcad.dll"));
            Assert.Equal(1, _cli.Attach("2026"));
            Assert.Contains("build", _out.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Detach_RemovesTheYear_AndTheBundleWhenItWasTheLastOne()
        {
            _cli.Attach("2026");

            var exit = _cli.Detach("2026");

            Assert.Equal(0, exit);
            Assert.False(Directory.Exists(Path.Combine(_bundleRoot, "Contents", "2026")));
            Assert.False(Directory.Exists(_bundleRoot));
        }

        [Fact]
        public void Detach_WhenNotAttached_SaysSo_AndSucceeds()
        {
            Assert.Equal(0, _cli.Detach("2026"));
            Assert.Contains("not attached", _out.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExtensionsAdd_CreatesConfig_AndListShowsIt()
        {
            var extDir = Path.Combine(_dir, "MyTools");
            Directory.CreateDirectory(extDir);

            Assert.Equal(0, _cli.ExtensionsAdd(extDir));
            Assert.Equal(0, _cli.ExtensionsList());

            Assert.Contains(extDir, _out.ToString());
            Assert.Contains(extDir, PyAcadConfig.Load(_configPath).ExtensionPaths);
        }

        [Fact]
        public void ExtensionsAdd_PreservesOtherConfigKeys_AndDeduplicates()
        {
            File.WriteAllText(_configPath, "{ \"runtime2026\": \"D:\\\\r\", \"extensions\": [\"D:\\\\existing\"] }");
            var extDir = Path.Combine(_dir, "MyTools");
            Directory.CreateDirectory(extDir);

            Assert.Equal(0, _cli.ExtensionsAdd(extDir));
            Assert.Equal(0, _cli.ExtensionsAdd(extDir)); // second add must not duplicate

            var config = PyAcadConfig.Load(_configPath);
            Assert.Equal(new[] { @"D:\existing", extDir }, config.ExtensionPaths);
            Assert.Contains("runtime2026", File.ReadAllText(_configPath));
        }

        [Fact]
        public void Env_ReportsVersionAndAttachmentState()
        {
            _cli.Attach("2026");

            Assert.Equal(0, _cli.Env());

            var text = _out.ToString();
            Assert.Contains("2026", text);
            Assert.Contains("attached", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void NewestAttachedYear_PicksTheHighestAttachedYear()
        {
            foreach (var year in new[] { "2025", "2026" })
            {
                var dir = Path.Combine(_bundleRoot, "Contents", year);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "PyAcad.dll"), "fake loader");
            }
            Directory.CreateDirectory(Path.Combine(_bundleRoot, "Contents", "2027")); // no loader inside

            Assert.Equal("2026", _cli.NewestAttachedYear());
            Assert.True(_cli.IsAttached("2025"));
            Assert.False(_cli.IsAttached("2027"));
        }

        [Fact]
        public void NewestAttachedYear_SkipsAYearWhoseAutoCADIsNotInstalled()
        {
            // A loader left in the bundle for a year with no AutoCAD on this PC (an
            // API-only reference drop) must not become the default pyacad run picks.
            foreach (var year in new[] { "2026", "2027" })
            {
                var dir = Path.Combine(_bundleRoot, "Contents", year);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "PyAcad.dll"), "fake loader");
            }

            Assert.Equal("2026", _cli.NewestAttachedYear()); // the resolver knows only 2026
        }

        [Fact]
        public void NewestAttachedYear_IsNull_WhenNothingIsAttached()
        {
            Assert.Null(_cli.NewestAttachedYear());
            Assert.False(_cli.IsAttached("2026"));
        }
    }
}
