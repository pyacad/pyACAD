using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Engine;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// The pure halves behind the Cleanup panel (Drawing Cleanup, Font and Proxy Report),
    /// all in pyacad.audit, run through the real IronPython engine: the cleanup step plan,
    /// its report and toast line, missing-font detection, the font remap plan and the
    /// proxy report. The new API-half functions are pinned to fail with the helpful
    /// ImportError outside AutoCAD, like every other drawing function.
    /// </summary>
    public class PyAcadApiCleanupTests
    {
        private readonly IronPythonEngine _engine;

        public PyAcadApiCleanupTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyAcadLibTests.PyAcadLibDir);
            _engine.Initialize(config);
        }

        private ExecResult Run(string code, IDictionary<string, object> globals = null)
        {
            var request = new ScriptRequest { Code = code };
            if (globals != null)
                foreach (var pair in globals) request.Globals[pair.Key] = pair.Value;
            return _engine.Execute(request);
        }

        [Theory]
        [InlineData("audit.erase(['2F3A'])")]
        [InlineData("audit.colors_to_bylayer()")]
        [InlineData("audit.text_style_fonts()")]
        [InlineData("audit.font_finder()")]
        [InlineData("audit.set_text_style_font('Notes', 'simplex.shx')")]
        [InlineData("audit.proxy_details()")]
        public void NewApiFunctions_RaiseHelpfulImportError_OutsideAutoCAD(string call)
        {
            var r = Run(
                "from pyacad import audit\n" +
                "try:\n" +
                "    " + call + "\n" +
                "    raise AssertionError('no ImportError')\n" +
                "except ImportError as e:\n" +
                "    assert 'AutoCAD' in str(e), str(e)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void CleanupPlan_ResolvesTicksIntoTheFixedStepOrder()
        {
            var r = Run(
                "from pyacad import audit\n" +
                "assert [s[0] for s in audit.STEPS] == ['purge', 'audit', 'zero_length', 'duplicates', 'bylayer', 'zoom']\n" +
                "assert all(isinstance(s[1], str) and s[1] for s in audit.STEPS)\n" +
                // nothing saved yet: every step ticked by default except ByLayer colour
                "assert audit.cleanup_plan(None, None) == ['purge', 'audit', 'zero_length', 'duplicates', 'zoom']\n" +
                // a list is exactly what was ticked, put in run order, unknown and repeated keys dropped
                "assert audit.cleanup_plan(['zoom', 'purge', 'zoom', 'nonsense'], None) == ['purge', 'zoom']\n" +
                "assert audit.cleanup_plan(('ZOOM', ' Purge '), None) == ['purge', 'zoom']\n" +
                "assert audit.cleanup_plan([], None) == []\n" +
                // defaults stand in when nothing was chosen
                "assert audit.cleanup_plan(None, ['zoom', 'audit']) == ['audit', 'zoom']\n" +
                "assert audit.cleanup_plan(None, []) == []\n" +
                // a dict overrides the defaults one step at a time
                "assert audit.cleanup_plan({'bylayer': True, 'zoom': False}, None) == ['purge', 'audit', 'zero_length', 'duplicates', 'bylayer']\n" +
                "assert audit.cleanup_plan({'purge': False, 'bogus': True}, ['purge', 'zoom']) == ['zoom']\n" +
                "assert audit.step_label('bylayer') == dict((s[0], s[1]) for s in audit.STEPS)['bylayer']\n" +
                "assert audit.deleting_steps(['purge', 'audit', 'zoom']) == ['purge']\n" +
                "assert audit.deleting_steps(['zoom', 'duplicates', 'zero_length', 'bylayer']) == ['zero_length', 'duplicates']\n" +
                "assert audit.deleting_steps(['audit', 'bylayer', 'zoom']) == []\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void DuplicateExtras_KeepsTheFirstOfEachGroup()
        {
            var r = Run(
                "from pyacad import audit\n" +
                "assert audit.duplicate_extras([['A', 'B', 'C'], ['D', 'E'], ['F']]) == ['B', 'C', 'E']\n" +
                "assert audit.duplicate_extras([]) == []\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void GeometryKey_TellsInsertsWithDifferentAttributesApart()
        {
            // Two tags stacked at one point but carrying different values are not
            // duplicates: deleting one would lose its data.
            var r = Run(
                "from pyacad import audit\n" +
                "tag = {'type': 'INSERT', 'layer': 'M-TAG', 'name': 'VAV', 'position': (5.0, 5.0, 0.0),\n" +
                "       'rotation': 0.0, 'scale': (1.0, 1.0, 1.0), 'attributes': {'TAG': 'VAV-1'}}\n" +
                "same = dict(tag, attributes={'TAG': 'VAV-1'})\n" +
                "other = dict(tag, attributes={'TAG': 'VAV-2'})\n" +
                "assert audit.geometry_key(tag) == audit.geometry_key(same)\n" +
                "assert audit.geometry_key(tag) != audit.geometry_key(other)\n" +
                "hash(audit.geometry_key(tag))\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void FormatReport_WritesOneLinePerStepInRunOrder()
        {
            var r = Run(
                "from pyacad import audit\n" +
                "text = audit.format_report([\n" +
                "    ('purge', 14),\n" +
                "    ('audit', None),\n" +
                "    ('zero_length', {'count': 3, 'skipped': 2}),\n" +
                "    ('duplicates', {'count': 12, 'groups': 5}),\n" +
                "    ('bylayer', {'count': 0}),\n" +
                "    ('zoom', None),\n" +
                "])\n" +
                "expected = [\n" +
                "    'Purge: 14 unused items removed',\n" +
                "    'Audit: queued; it runs when this tool finishes and reports on the command line',\n" +
                "    'Zero-length: 3 entities deleted, 2 skipped on locked layers',\n" +
                "    'Duplicates: 12 extra copies deleted (5 groups)',\n" +
                "    'ByLayer colour: already ByLayer',\n" +
                "    'Zoom extents: queued',\n" +
                "]\n" +
                "assert text.splitlines() == expected, text\n" +
                "one = audit.format_report([('purge', 1), ('duplicates', {'count': 1, 'groups': 1}), ('bylayer', {'count': 1})])\n" +
                "assert one.splitlines() == ['Purge: 1 unused item removed', 'Duplicates: 1 extra copy deleted (1 group)',\n" +
                "                            'ByLayer colour: 1 entity changed'], one\n" +
                "none = audit.format_report([('purge', 0), ('zero_length', {'count': 0}), ('duplicates', {'count': 0, 'skipped': 4})])\n" +
                "assert none.splitlines() == ['Purge: nothing unused', 'Zero-length: none found',\n" +
                "                             'Duplicates: none deleted, 4 skipped on locked layers'], none\n" +
                "odd = audit.format_report([('duplicates', {'error': 'eLockViolation'}), ('bylayer', {'cancelled': True}),\n" +
                "                           ('zoom', {'not_run': True}), ('mystery', 7)])\n" +
                "assert odd.splitlines() == ['Duplicates: failed (eLockViolation)', 'ByLayer colour: cancelled',\n" +
                "                            'Zoom extents: not run', 'mystery: 7'], odd\n" +
                "assert audit.format_report([]) == 'No steps ran.'\n" +
                "assert u'\\u2014' not in text\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Summary_IsOneLineForTheToast()
        {
            var r = Run(
                "from pyacad import audit\n" +
                "line = audit.summary([('purge', 14), ('audit', None), ('zero_length', {'count': 3}),\n" +
                "                      ('duplicates', {'count': 12, 'groups': 5}), ('bylayer', {'count': 240}), ('zoom', None)])\n" +
                "assert line == '14 purged, 3 zero-length deleted, 12 duplicates deleted, 240 set to ByLayer', line\n" +
                "assert audit.summary([('purge', 0), ('audit', None), ('zoom', None)]) == 'Nothing needed cleaning'\n" +
                "failed = audit.summary([('purge', 2), ('duplicates', {'error': 'x'}), ('zoom', {'error': 'y'})])\n" +
                "assert failed == '2 purged, 2 steps failed', failed\n" +
                "assert audit.summary([('bylayer', {'cancelled': True})]) == 'Cancelled'\n" +
                "assert '\\n' not in line\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void MissingFonts_ChecksEachStyleAgainstASetOfFileNames()
        {
            var r = Run(
                "from pyacad import audit\n" +
                "styles = [('Standard', 'txt'), ('Notes', 'romans.shx'), ('Title', 'C:\\\\Fonts\\\\Fancy.TTF'),\n" +
                "          ('Blank', ''), ('NoFile', None), ('Arial', 'arial.ttf'), ('Again', 'ROMANS.SHX')]\n" +
                // names compare case-insensitively on the bare file name; no extension means .shx
                "available = set(['txt.shx', 'D:\\\\Support\\\\Arial.TTF'])\n" +
                "rows = audit.missing_fonts(styles, available)\n" +
                "assert rows == [('Notes', 'romans.shx'), ('Title', 'C:\\\\Fonts\\\\Fancy.TTF'), ('Again', 'ROMANS.SHX')], rows\n" +
                "assert audit.missing_fonts(styles, ['fancy.ttf', 'romans.shx', 'txt', 'arial.ttf']) == []\n" +
                "assert audit.missing_fonts([], set()) == []\n" +
                // extra columns ride along untouched
                "assert audit.missing_fonts([('JIS', 'extfont2.shx', 'big font')], frozenset()) == [('JIS', 'extfont2.shx', 'big font')]\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void MissingFonts_AsksACallableOncePerFont()
        {
            var r = Run(
                "from pyacad import audit\n" +
                "calls = []\n" +
                "def finder(name):\n" +
                "    calls.append(name)\n" +
                "    return name.lower().startswith('romans')\n" +
                "styles = [('Standard', 'txt'), ('Notes', 'romans.shx'), ('Again', 'ROMANS.SHX'), ('Title', 'fancy.ttf'), ('Blank', '')]\n" +
                "rows = audit.missing_fonts(styles, finder)\n" +
                "assert rows == [('Standard', 'txt'), ('Title', 'fancy.ttf')], rows\n" +
                "assert calls == ['txt', 'romans.shx', 'fancy.ttf'], calls\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void RemapPlan_GroupsMissingTextFontsBySHXAndTrueType()
        {
            var r = Run(
                "from pyacad import audit\n" +
                "assert audit.font_kind('romans.shx') == 'shx'\n" +
                "assert audit.font_kind('gost') == 'shx'\n" +
                "assert audit.font_kind('C:\\\\Fonts\\\\Fancy.TTF') == 'ttf'\n" +
                "assert audit.font_kind('msgothic.ttc') == 'ttf'\n" +
                "assert audit.font_kind('font.otf') == 'ttf'\n" +
                "missing = [('Notes', 'romans.shx', 'font'), ('Title', 'fancy.ttf', 'font'),\n" +
                "           ('JIS', 'extfont2.shx', 'big font'), ('', 'ltypeshp.shx', 'shape'),\n" +
                "           ('Plain', 'gost'), ('Notes', 'romans.shx', 'font'), ('Otf', 'x.OTF')]\n" +
                "plan = audit.remap_plan(missing)\n" +
                "assert plan == [('shx', 'simplex.shx', ['Notes', 'Plain']), ('ttf', 'arial.ttf', ['Title', 'Otf'])], plan\n" +
                "assert audit.remap_plan([('Only', 'a.ttf')]) == [('ttf', 'arial.ttf', ['Only'])]\n" +
                "assert audit.remap_plan([('JIS', 'extfont2.shx', 'big font')]) == []\n" +
                "assert audit.remap_plan([]) == []\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        private const string ProxyFixture =
            "details = [\n" +
            "    {'handle': '1A', 'kind': 'entity', 'class': 'AecDbWall', 'application': 'AutoCAD Architecture', 'where': 'Model'},\n" +
            "    {'handle': '1B', 'kind': 'entity', 'class': 'AecDbWall', 'application': 'AutoCAD Architecture', 'where': 'Layout1'},\n" +
            "    {'handle': '2C', 'kind': 'object', 'class': 'AecDbDispProps', 'application': '', 'where': 'Named objects'},\n" +
            "    {'handle': '3D', 'kind': 'entity', 'class': '', 'application': 'Mystery app', 'where': 'BLOCK:Pump'},\n" +
            "]\n" +
            "missing = [('Notes', 'romans.shx', 'font'), ('JIS', 'extfont2.shx', 'big font'), ('', 'ltypeshp.shx', 'shape')]\n";

        [Fact]
        public void ProxySummary_CountsByClassMostFirst()
        {
            var r = Run(
                "from pyacad import audit\n" + ProxyFixture +
                "rows = audit.proxy_summary(details)\n" +
                "assert rows == [('AecDbWall', 'AutoCAD Architecture', 2), ('(unknown class)', 'Mystery app', 1),\n" +
                "                ('AecDbDispProps', '', 1)], rows\n" +
                "assert audit.proxy_summary([]) == []\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void FormatFontReport_ListsProxiesThenMissingFonts()
        {
            var r = Run(
                "from pyacad import audit\n" + ProxyFixture +
                "text = audit.format_font_report(details, missing)\n" +
                "expected = [\n" +
                "    '3 proxy entities, 1 proxy object',\n" +
                "    '  AecDbWall (AutoCAD Architecture): 2',\n" +
                "    '  (unknown class) (Mystery app): 1',\n" +
                "    '  AecDbDispProps: 1',\n" +
                "    '',\n" +
                "    '3 fonts not found in the support paths',\n" +
                "    '  Notes: romans.shx',\n" +
                "    '  JIS: extfont2.shx (big font)',\n" +
                "    '  Shape file: ltypeshp.shx',\n" +
                "]\n" +
                "assert text.splitlines() == expected, text\n" +
                "clean = audit.format_font_report([], [])\n" +
                "assert clean.splitlines() == ['No proxies.', '', 'Every font the text styles use was found.'], clean\n" +
                "one = audit.format_font_report(details[:1], missing[:1])\n" +
                "assert one.splitlines()[0] == '1 proxy entity' and one.splitlines()[3] == '1 font not found in the support paths', one\n" +
                // long lists are cut short; the Excel export carries the rest
                "many = [('S%d' % i, 'f%d.shx' % i, 'font') for i in range(20)]\n" +
                "cut = audit.format_font_report([], many, limit=5).splitlines()\n" +
                "assert cut[-1] == '  ...and 15 more', cut\n" +
                "assert len(cut) == 3 + 5 + 1, cut\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void FontReportRows_GiveOneSpreadsheetRowPerFinding()
        {
            var r = Run(
                "from pyacad import audit\n" + ProxyFixture +
                "assert audit.FONT_REPORT_HEADERS == ['Issue', 'Name', 'Detail', 'Where', 'Handle']\n" +
                "rows = audit.font_report_rows(details, missing)\n" +
                "assert rows == [\n" +
                "    ['Proxy entity', 'AecDbWall', 'AutoCAD Architecture', 'Model', '1A'],\n" +
                "    ['Proxy entity', 'AecDbWall', 'AutoCAD Architecture', 'Layout1', '1B'],\n" +
                "    ['Proxy object', 'AecDbDispProps', '', 'Named objects', '2C'],\n" +
                "    ['Proxy entity', '(unknown class)', 'Mystery app', 'BLOCK:Pump', '3D'],\n" +
                "    ['Missing font', 'Notes', 'romans.shx', '', ''],\n" +
                "    ['Missing big font', 'JIS', 'extfont2.shx', '', ''],\n" +
                "    ['Missing shape file', '', 'ltypeshp.shx', '', ''],\n" +
                "], rows\n" +
                "assert all(len(row) == len(audit.FONT_REPORT_HEADERS) for row in rows)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void CleanupPanel_ShipsBothTools_ParsedIconedAndCompiling()
        {
            var model = BundleParser.ParseExtension(ShippedExtensionDir());
            Assert.DoesNotContain(model.Problems,
                p => p.IndexOf("Cleanup.panel", StringComparison.OrdinalIgnoreCase) >= 0);

            var panel = model.Tabs.Single().Panels.Single(p => p.Title == "Cleanup");
            var buttons = panel.Items.OfType<PushButtonModel>().ToList();
            Assert.Equal(new[] { "Drawing Cleanup", "Font and Proxy Report" }, buttons.Select(b => b.Title).ToArray());
            Assert.Equal(new[] { "CC", "CF" }, buttons.Select(b => b.KeyTipOverride).ToArray());
            Assert.All(buttons, b =>
            {
                Assert.False(string.IsNullOrWhiteSpace(b.Tooltip));
                Assert.NotNull(b.ContextRule);
                foreach (var icon in new[] { "icon.png", "icon.dark.png", "icon.small.png", "icon.small.dark.png" })
                    Assert.True(File.Exists(Path.Combine(b.Directory, icon)), "missing " + icon + " in " + b.Directory);
            });
            Assert.NotNull(buttons[0].ConfigScriptPath);       // Shift+Click forgets the remembered steps
            Assert.Null(buttons[1].ConfigScriptPath);

            foreach (var path in buttons.SelectMany(b => new[] { b.ScriptPath, b.ConfigScriptPath }).Where(p => p != null))
            {
                var r = Run("with open(__source_path__) as f:\n" +
                            "    compile(f.read(), __source_path__, 'exec')\n",
                    new Dictionary<string, object> { ["__source_path__"] = path });
                Assert.True(r.Succeeded, path + " failed to compile:\n" + r.ErrorText);
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
    }
}
