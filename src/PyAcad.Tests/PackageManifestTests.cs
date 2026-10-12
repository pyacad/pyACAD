using System;
using System.IO;
using System.Linq;
using PyAcad.Runtime.Install;
using Xunit;

namespace PyAcad.Tests
{
    public class PackageManifestTests
    {
        [Fact]
        public void Register_Creates_The_Year_Block_With_Its_Series()
        {
            var doc = PackageManifest.CreateEmpty("0.4.0");

            PackageManifest.Register(doc, "2026", "PyAcad.dll", "pyACAD", "0.4.0");

            var block = Assert.Single(doc.Root.Elements("Components"));
            var req = block.Element("RuntimeRequirements");
            Assert.Equal("R25.1", (string)req.Attribute("SeriesMin"));
            Assert.Equal("R25.1", (string)req.Attribute("SeriesMax"));
            Assert.Equal("AutoCAD*", (string)req.Attribute("Platform"));
            var entry = Assert.Single(block.Elements("ComponentEntry"));
            Assert.Equal("./Contents/2026/PyAcad.dll", (string)entry.Attribute("ModuleName"));
            Assert.Equal(".Net", (string)entry.Attribute("AppType"));
            Assert.Equal("True", (string)entry.Attribute("LoadOnAutoCADStartup"));
        }

        [Fact]
        public void Register_Twice_Adds_One_Entry_And_A_Second_Dll_Joins_The_Same_Block()
        {
            var doc = PackageManifest.CreateEmpty("0.4.0");
            PackageManifest.Register(doc, "2026", "PyAcad.dll", "pyACAD", "0.4.0");
            PackageManifest.Register(doc, "2026", "PyAcad.dll", "pyACAD", "0.4.0");
            PackageManifest.Register(doc, "2026", "PyAcadExtra.dll", "pyACAD extra", "0.4.0");

            var block = Assert.Single(doc.Root.Elements("Components"));
            Assert.Equal(2, block.Elements("ComponentEntry").Count());
        }

        [Fact]
        public void Unregister_Removes_Only_That_Entry_And_Drops_An_Emptied_Block()
        {
            var doc = PackageManifest.CreateEmpty("0.4.0");
            PackageManifest.Register(doc, "2026", "PyAcad.dll", "pyACAD", "0.4.0");
            PackageManifest.Register(doc, "2026", "PyAcadExtra.dll", "pyACAD extra", "0.4.0");
            PackageManifest.Register(doc, "2025", "PyAcad.dll", "pyACAD", "0.4.0");

            Assert.True(PackageManifest.Unregister(doc, "2026", "PyAcadExtra.dll"));
            Assert.False(PackageManifest.Unregister(doc, "2026", "PyAcadExtra.dll"));
            Assert.Single(PackageManifest.FindYear(doc, "2026").Elements("ComponentEntry"));

            Assert.True(PackageManifest.Unregister(doc, "2025", "PyAcad.dll"));
            Assert.Null(PackageManifest.FindYear(doc, "2025"));
            Assert.False(PackageManifest.IsEmpty(doc));
        }

        [Fact]
        public void Save_And_Load_Round_Trip_With_Declaration()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pyacad_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "PackageContents.xml");
            try
            {
                var doc = PackageManifest.CreateEmpty("0.4.0");
                PackageManifest.Register(doc, "2024", "PyAcad.dll", "pyACAD", "0.4.0");
                PackageManifest.Save(doc, path);
                PackageManifest.Save(doc, path);   // overwrite path too

                var text = File.ReadAllText(path);
                Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text);
                Assert.False(File.Exists(path + ".tmp"));
                var back = PackageManifest.Load(path);
                Assert.Equal("R24.3", (string)PackageManifest.FindYear(back, "2024").Element("RuntimeRequirements").Attribute("SeriesMin"));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void BundleLayout_Recognises_Only_The_Bundle_Shape()
        {
            var inBundle = BundleLayout.FromLoaderPath(@"C:\Users\x\AppData\Roaming\Autodesk\ApplicationPlugins\pyACAD.bundle\Contents\2026\PyAcad.dll");
            Assert.NotNull(inBundle);
            Assert.Equal("2026", inBundle.Year);
            Assert.EndsWith(@"pyACAD.bundle", inBundle.Root);
            Assert.EndsWith(@"pyACAD.bundle\Contents\2026", inBundle.ContentsDir);
            Assert.EndsWith(@"pyACAD.bundle\PackageContents.xml", inBundle.ManifestPath);

            Assert.Null(BundleLayout.FromLoaderPath(@"C:\Program Files\Autodesk\AutoCAD 2026\PyAcad\PyAcad.dll"));
            Assert.Null(BundleLayout.FromLoaderPath(@"D:\repo\bin\2026\PyAcad.dll"));
            Assert.Null(BundleLayout.FromLoaderPath(@"X:\Other.bundle\Contents\notayear\PyAcad.dll"));
            Assert.Null(BundleLayout.FromLoaderPath(null));
        }

        [Fact]
        public void SeriesFor_Is_AutoCADs_Release_Number()
        {
            Assert.Equal("R24.0", BundleLayout.SeriesFor("2021"));
            Assert.Equal("R24.2", BundleLayout.SeriesFor("2023"));
            Assert.Equal("R25.0", BundleLayout.SeriesFor("2025"));
            Assert.Equal("R25.1", BundleLayout.SeriesFor("2026"));
            Assert.Throws<ArgumentException>(() => BundleLayout.SeriesFor("2027"));
        }
    }
}
