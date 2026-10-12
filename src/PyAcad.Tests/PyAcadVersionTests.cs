using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using PyAcad.Runtime;
using PyAcad.Runtime.Config;
using PyAcad.Runtime.Forms;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// One version number, stamped into every assembly from pyacad/__init__.py at
    /// build, and shown wherever someone debugging an install would look: the boot
    /// log, the Settings window, and the host object scripts see. The number is plain
    /// MAJOR.MINOR.PATCH and CHANGELOG.md leads with it, so a release cannot be built
    /// without its notes.
    /// </summary>
    public class PyAcadVersionTests
    {
        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "pyacadlib", "pyacad", "__init__.py")))
                    return dir.FullName;
            throw new FileNotFoundException("pyacad/__init__.py not found above the test dir");
        }

        private static string RepoVersion()
        {
            var init = Path.Combine(RepoRoot(), "pyacadlib", "pyacad", "__init__.py");
            return Regex.Match(File.ReadAllText(init), @"__version__\s*=\s*['""]([^'""]+)").Groups[1].Value;
        }

        /// <summary>The first release heading in CHANGELOG.md: "## [x.y.z] - yyyy-mm-dd"
        /// once released, "## x.y.z (unreleased)" while it is still being made.</summary>
        private static Match FirstReleaseHeading()
        {
            var changelog = Path.Combine(RepoRoot(), "CHANGELOG.md");
            Assert.True(File.Exists(changelog), "CHANGELOG.md is missing from the repo root");
            var match = Regex.Match(File.ReadAllText(changelog),
                @"^## (?:\[(?<version>[^\]]+)\] - (?<date>\S+)|(?<version>\d+\.\d+\.\d+) \((?<unreleased>unreleased)\))\s*$",
                RegexOptions.Multiline);
            Assert.True(match.Success,
                "CHANGELOG.md has no '## [x.y.z] - yyyy-mm-dd' or '## x.y.z (unreleased)' heading");
            return match;
        }

        [Fact]
        public void TheProductVersion_IsTheOneInTheLibrary()
        {
            Assert.Equal(RepoVersion(), PyAcadVersion.Product);
        }

        /// <summary>Autodesk's PackageContents.xml and the installer's version resource
        /// both take digits and dots only, so no -beta or +build suffix ever goes in.</summary>
        [Fact]
        public void TheVersion_IsPlainMajorMinorPatch()
        {
            Assert.Matches(@"^\d+\.\d+\.\d+$", RepoVersion());
        }

        [Fact]
        public void TheChangelog_LeadsWithTheCurrentVersion()
        {
            Assert.Equal(RepoVersion(), FirstReleaseHeading().Groups["version"].Value);
        }

        [Fact]
        public void TheChangelog_DatesTheCurrentRelease()
        {
            var heading = FirstReleaseHeading();
            if (heading.Groups["unreleased"].Success) return;   // dated when it ships
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", heading.Groups["date"].Value);
        }

        [Fact]
        public void TheSummary_NamesVersionHostAndRuntimeFolder()
        {
            var summary = PyAcadVersion.Summary();

            Assert.StartsWith("pyACAD " + PyAcadVersion.Product, summary);
            Assert.Contains("AutoCAD", summary);
            Assert.Contains("PyAcad.Runtime.dll", summary);
        }

        [Fact]
        public void TheSettingsWindow_ShowsTheVersionLine()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = SettingsDialog.Build(new PyAcadConfig.UserSettings());
                    Assert.Contains(PyAcadVersion.Product, SettingsDialog.VersionLineOf(window));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }
    }
}
