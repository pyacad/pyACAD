using System.Collections.Generic;
using PyAcad.Runtime.Upgrade;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// The two things the runtime says about users' work in pyACAD.extension: once
    /// after an update, what the installer kept and where; and at startup, before the
    /// next update, that there is work in a folder updates replace.
    /// </summary>
    public class UpgradeNoticesTests
    {
        private static Dictionary<string, object> Report(int moved, int backedUpOnly, int edited) =>
            new Dictionary<string, object>
            {
                ["version"] = "1.2.0",
                ["backup"] = @"C:\Users\x\AppData\Roaming\pyACAD\backups\pyACAD.extension before 1.2.0 (2026-09-29 1432)",
                ["moved"] = Paths(moved),
                ["backedUpOnly"] = Paths(backedUpOnly),
                ["edited"] = Paths(edited),
            };

        private static object[] Paths(int n)
        {
            var list = new object[n];
            for (var i = 0; i < n; i++) list[i] = "file" + i;
            return list;
        }

        [Fact]
        public void AfterAnUpdate_SaysWhatMovedAndWhereTheBackupIs()
        {
            var (title, detail) = UpgradeNotices.ReportMessage(Report(3, 0, 0));
            Assert.Equal("pyACAD 1.2.0 kept your work", title);
            Assert.Equal("3 files you had added to pyACAD.extension moved to My pyACAD.extension. " +
                         "A copy of everything is in backups\\pyACAD.extension before 1.2.0 (2026-09-29 1432).", detail);
        }

        [Fact]
        public void AfterAnUpdate_NamesEditsAndFilesThatCouldOnlyBeBackedUp()
        {
            var (_, detail) = UpgradeNotices.ReportMessage(Report(1, 2, 4));
            Assert.Equal("1 file you had added to pyACAD.extension moved to My pyACAD.extension. " +
                         "2 added inside pyACAD buttons and 4 pyACAD files you had edited are only in the backup. " +
                         "A copy of everything is in backups\\pyACAD.extension before 1.2.0 (2026-09-29 1432).", detail);
        }

        [Fact]
        public void AtStartup_WarnsBeforeTheNextUpdate()
        {
            var (title, detail) = UpgradeNotices.ChangesMessage(added: 2, edited: 1);
            Assert.Equal("You have work inside pyACAD.extension", title);
            Assert.Equal("2 added and 1 edited file. Updates replace that folder: what you added moves to " +
                         "My pyACAD.extension and edits are only backed up. Keep your own tools in My pyACAD.extension.",
                detail);
        }

        [Fact]
        public void Fingerprint_ChangesOnlyWhenTheWorkDoes()
        {
            // The startup warning shows once per state of the folder, not every start.
            var a = UpgradeNotices.Fingerprint(new[] { new ShippedFiles.Change("b.py", true), new ShippedFiles.Change("a.py", false) });
            var b = UpgradeNotices.Fingerprint(new[] { new ShippedFiles.Change("a.py", false), new ShippedFiles.Change("b.py", true) });
            var c = UpgradeNotices.Fingerprint(new[] { new ShippedFiles.Change("a.py", false) });
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }
    }
}
