using System;
using System.Collections.Generic;
using System.IO;
using PyAcad.Runtime.Engine;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// The pure halves behind the Data and Takeoff panels (blocks export, round trip,
    /// schedule, attribute find/replace, tag audit; lengths by layer), run through the
    /// real IronPython engine. The API halves they feed are pinned to import cleanly and
    /// to fail with the helpful ImportError from pyacad._api outside AutoCAD.
    /// </summary>
    public class PyAcadApiDataTests
    {
        private readonly IronPythonEngine _engine;

        public PyAcadApiDataTests()
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

        // Three references the export tests share: two blocks with overlapping tags and
        // one without attributes, deliberately out of name order.
        private const string Refs =
            "import math\n" +
            "refs = [\n" +
            "    {'handle': '2B', 'name': 'VAV', 'layer': 'M-EQPM', 'space': 'Model', 'position': (10.0, 20.0, 0.0),\n" +
            "     'rotation': math.pi / 2, 'scale': (1.0, 1.0, 1.0), 'attributes': {'TAG': 'VAV-1', 'CFM': '400'},\n" +
            "     'tags': ['TAG', 'CFM']},\n" +
            "    {'handle': '1F', 'name': 'AHU', 'layer': 'M-EQPM', 'space': 'Layout1', 'position': (0.0, 0.0, 0.0),\n" +
            "     'rotation': 0.0, 'scale': (2.0, 2.0, 1.0), 'attributes': {'TAG': 'AHU-1', 'MODEL': 'X9'},\n" +
            "     'tags': ['TAG', 'MODEL']},\n" +
            "    {'handle': 'A0', 'name': 'North', 'layer': '0', 'space': 'Model', 'position': (1.0, 2.0, 3.0),\n" +
            "     'rotation': -math.pi / 2, 'scale': (1.0, 1.0, 1.0), 'attributes': {}},\n" +
            "]\n";

        [Theory]
        [InlineData("blocks.snapshots()")]
        [InlineData("blocks.snapshots(['2F3A'])")]
        [InlineData("blocks.apply_changes({'1A': {'layer': '0'}})")]
        [InlineData("blocks.insert_table((0, 0), ['Block', 'Count'], [['VAV', 3]], title='Schedule')")]
        [InlineData("ents.length_of('2F3A')")]
        [InlineData("ents.lengths(['2F3A'])")]
        public void DataApiFunctions_RaiseHelpfulImportError_OutsideAutoCAD(string call)
        {
            var r = Run(
                "from pyacad import blocks, ents\n" +
                "try:\n" +
                "    " + call + "\n" +
                "    raise AssertionError('no ImportError')\n" +
                "except ImportError as e:\n" +
                "    assert 'AutoCAD' in str(e), str(e)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Blocks_ApiHalves_RejectBadInput_BeforeTouchingAutoCAD()
        {
            var r = Run(
                "from pyacad import blocks\n" +
                "for bad in (lambda: blocks.insert_table((0, 0), [], []),\n" +
                "            lambda: blocks.insert_table((0, 0), ['A'], [['x', 'too many']]),\n" +
                "            lambda: blocks.insert_table('here', ['A'], [['x']]),\n" +
                "            lambda: blocks.apply_changes(['1A']),\n" +
                "            lambda: blocks.apply_changes({'1A': 'TAG=x'})):\n" +
                "    try:\n" +
                "        bad()\n" +
                "        raise AssertionError('accepted')\n" +
                "    except ValueError:\n" +
                "        pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Blocks_AttributeTags_AreTheUnion_InFirstSeenOrder()
        {
            var r = Run(Refs +
                "from pyacad import blocks\n" +
                "assert blocks.attribute_tags(refs) == ['TAG', 'CFM', 'MODEL'], blocks.attribute_tags(refs)\n" +
                "# no 'tags' list: that ref's tags go in sorted; a tag differing only in case counts once\n" +
                "more = [{'attributes': {'ROOM': '1', 'AREA': '2'}}, {'attributes': {'Room': '3', 'TAG': 'x'}}]\n" +
                "assert blocks.attribute_tags(more) == ['AREA', 'ROOM', 'TAG'], blocks.attribute_tags(more)\n" +
                "assert blocks.attribute_tags([]) == []\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Blocks_ExportRows_OneRowPerReference_SortedByBlock_WithEveryTagAColumn()
        {
            var r = Run(Refs +
                "from pyacad import blocks\n" +
                "headers, rows = blocks.export_rows(refs)\n" +
                "assert headers == ['Handle', 'Block', 'Layer', 'Space', 'X', 'Y', 'Z', 'Rotation', 'Scale',\n" +
                "                   'TAG', 'CFM', 'MODEL'], headers\n" +
                "assert rows[0] == ['1F', 'AHU', 'M-EQPM', 'Layout1', 0.0, 0.0, 0.0, 0.0, '2, 2, 1', 'AHU-1', '', 'X9'], rows[0]\n" +
                "assert rows[1] == ['A0', 'North', '0', 'Model', 1.0, 2.0, 3.0, 270.0, 1.0, '', '', ''], rows[1]\n" +
                "assert rows[2] == ['2B', 'VAV', 'M-EQPM', 'Model', 10.0, 20.0, 0.0, 90.0, 1.0, 'VAV-1', '400', ''], rows[2]\n" +
                "assert blocks.export_rows([]) == (list(blocks.EXPORT_COLUMNS), [])\n" +
                "# a ref whose tag differs in case from the column still lands in it\n" +
                "h, rs = blocks.export_rows([dict(refs[0], attributes={'Tag': 'v'}, tags=['Tag']), refs[1]])\n" +
                "assert h[9:] == ['Tag', 'MODEL'] and rs[1][9] == 'v', (h, rs)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Blocks_DiffRows_OfAnUntouchedExport_IsEmpty()
        {
            var r = Run(Refs +
                "from pyacad import blocks\n" +
                "headers, rows = blocks.export_rows(refs)\n" +
                "assert blocks.diff_rows(refs, [headers] + rows) == ({}, [])\n" +
                "# what Excel hands back: numbers as float, short rows where trailing cells are empty\n" +
                "back = [headers] + [list(row) for row in rows]\n" +
                "back[2] = back[2][:9]\n" +
                "back[3][10] = 400.0\n" +
                "back[1][0] = '1f'\n" +
                "assert blocks.diff_rows(refs, back) == ({}, []), blocks.diff_rows(refs, back)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Blocks_DiffRows_ReportsChangedValuesAndLayer_AndSkipsHandlesNoLongerThere()
        {
            var r = Run(Refs +
                "from pyacad import blocks\n" +
                "headers, rows = blocks.export_rows(refs)\n" +
                "sheet = [headers] + [list(row) for row in rows]\n" +
                "sheet[1][9] = 'AHU-2'\n" +
                "sheet[1][2] = 'M-EQPM-NEW'\n" +
                "sheet[2][9] = 'ignored'\n" +           // North has no TAG attribute to write
                "sheet[2][2] = ''\n" +                  // a blank layer is not a change
                "sheet[3][10] = ''\n" +                 // a cleared value is
                "sheet[3][4] = 999.0\n" +               // position is not written back
                "sheet.append(['FFFF', 'Gone', '0', 'Model'])\n" +
                "sheet.append(['', 'no handle'])\n" +
                "sheet.append([])\n" +
                "sheet.append(['FFFF'])\n" +
                "changes, skipped = blocks.diff_rows(refs, sheet)\n" +
                "assert changes == {'1F': {'attributes': {'TAG': 'AHU-2'}, 'layer': 'M-EQPM-NEW'},\n" +
                "                   '2B': {'attributes': {'CFM': ''}}}, changes\n" +
                "assert skipped == ['FFFF'], skipped\n" +
                "# columns are found by name, so a reordered sheet still reads\n" +
                "swapped = [['CFM', 'layer', 'HANDLE'], [500.0, 'M-EQPM', '2b'], ['x', 'L', 'not-hex']]\n" +
                "changes, skipped = blocks.diff_rows(refs, swapped)\n" +
                "assert changes == {'2B': {'attributes': {'CFM': '500'}}}, changes\n" +
                "assert skipped == ['not-hex'], skipped\n" +
                "for bad in ([], [['Block', 'TAG'], ['VAV', 'x']]):\n" +
                "    try:\n" +
                "        blocks.diff_rows(refs, bad)\n" +
                "        raise AssertionError(bad)\n" +
                "    except ValueError:\n" +
                "        pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Blocks_ScheduleRows_CountByName_OrByNameAndLayer()
        {
            var r = Run(
                "from pyacad import blocks\n" +
                "refs = [{'name': 'VAV', 'layer': 'M-2'}, {'name': 'ahu', 'layer': 'M-1'},\n" +
                "        {'name': 'VAV', 'layer': 'M-1'}, {'name': 'VAV', 'layer': 'M-2'}]\n" +
                "assert blocks.schedule_rows(refs) == (['Block', 'Count'], [['ahu', 1], ['VAV', 3]]), blocks.schedule_rows(refs)\n" +
                "assert blocks.schedule_rows(refs, by_layer=True) == (\n" +
                "    ['Block', 'Layer', 'Count'], [['ahu', 'M-1', 1], ['VAV', 'M-1', 1], ['VAV', 'M-2', 2]])\n" +
                "assert blocks.schedule_rows([]) == (['Block', 'Count'], [])\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Blocks_ReplaceInAttributes_HonoursCaseAndTag_AndTreatsTextLiterally()
        {
            var r = Run(
                "from pyacad import blocks\n" +
                "refs = [{'handle': '1A', 'attributes': {'TAG': 'AHU-1', 'DESC': 'Ahu supply'}},\n" +
                "        {'handle': '1B', 'attributes': {'TAG': 'VAV-1'}},\n" +
                "        {'handle': '1C', 'attributes': {}}]\n" +
                "assert blocks.replace_in_attributes(refs, 'AHU', 'RTU') == {'1A': {'TAG': 'RTU-1'}}\n" +
                "got = blocks.replace_in_attributes(refs, 'ahu', 'RTU', case_sensitive=False)\n" +
                "assert got == {'1A': {'TAG': 'RTU-1', 'DESC': 'RTU supply'}}, got\n" +
                "got = blocks.replace_in_attributes(refs, 'ahu', 'RTU', case_sensitive=False, tag='desc')\n" +
                "assert got == {'1A': {'DESC': 'RTU supply'}}, got\n" +
                "got = blocks.replace_in_attributes(refs, '-1', '', tag='TAG')\n" +
                "assert got == {'1A': {'TAG': 'AHU'}, '1B': {'TAG': 'VAV'}}, got\n" +
                "assert blocks.replace_in_attributes(refs, 'zzz', 'y') == {}\n" +
                "dots = [{'handle': '2A', 'attributes': {'P': 'a.b A.B axb'}}]\n" +
                "got = blocks.replace_in_attributes(dots, 'a.b', '\\\\1', case_sensitive=False)\n" +
                "assert got == {'2A': {'P': '\\\\1 \\\\1 axb'}}, got\n" +
                "try:\n" +
                "    blocks.replace_in_attributes(refs, '', 'x')\n" +
                "    raise AssertionError('empty find')\n" +
                "except ValueError:\n" +
                "    pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Blocks_TagAudit_FindsDuplicatesBlanksAndMissing()
        {
            var r = Run(
                "from pyacad import blocks\n" +
                "refs = [\n" +
                "    {'handle': '1', 'name': 'AHU', 'attributes': {'TAG': 'AHU-1'}},\n" +
                "    {'handle': '2', 'name': 'AHU', 'attributes': {'TAG': 'ahu-1 '}},\n" +
                "    {'handle': '3', 'name': 'VAV', 'attributes': {'TAG': 'VAV-1'}},\n" +
                "    {'handle': '4', 'name': 'VAV', 'attributes': {'TAG': '  '}},\n" +
                "    {'handle': '5', 'name': 'North', 'attributes': {}},\n" +
                "    {'handle': '6', 'name': 'VAV', 'attributes': {'Tag': 'VAV-1'}},\n" +
                "    {'handle': '7', 'name': 'VAV', 'attributes': {'TAG': 'VAV-2'}},\n" +
                "]\n" +
                "audit = blocks.tag_audit(refs, 'tag')\n" +
                "assert audit['duplicates'] == {'AHU-1': ['1', '2'], 'VAV-1': ['3', '6']}, audit\n" +
                "assert audit['blanks'] == ['4'] and audit['missing'] == ['5'], audit\n" +
                "assert audit['tagged'] == 5, audit\n" +
                "strict = blocks.tag_audit(refs, 'TAG', ignore_case=False)\n" +
                "assert strict['duplicates'] == {'VAV-1': ['3', '6']}, strict\n" +
                "clean = blocks.tag_audit(refs[2:3], 'TAG')\n" +
                "assert clean == {'duplicates': {}, 'blanks': [], 'missing': [], 'tagged': 1}, clean\n" +
                "try:\n" +
                "    blocks.tag_audit(refs, ' ')\n" +
                "    raise AssertionError('blank tag')\n" +
                "except ValueError:\n" +
                "    pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Blocks_ColumnWidths_FitTheLongestTextInEachColumn()
        {
            var r = Run(
                "from pyacad import blocks\n" +
                "w = blocks.column_widths(['Block', 'Count'], [['Supply diffuser', 12], ['VAV', 3]], 2.5)\n" +
                "margin = 2.5 / 3.0\n" +
                "assert abs(w[0] - (15 * 2.5 * 0.9 + 2 * margin)) < 1e-9, w\n" +
                "assert abs(w[1] - (5 * 2.5 * 0.9 + 2 * margin)) < 1e-9, w\n" +
                "w = blocks.column_widths(['N'], [[None]], 2.0, margin=1.0)\n" +
                "assert abs(w[0] - (3 * 2.0 * 0.9 + 2.0)) < 1e-9, w\n" +
                "# a title wider than the columns spreads the shortfall evenly across them\n" +
                "w = blocks.column_widths(['A', 'B'], [], 1.0, margin=0.0, title='x' * 20, title_height=2.0)\n" +
                "assert abs(sum(w) - 36.0) < 1e-9 and abs(w[0] - w[1]) < 1e-9, w\n" +
                "narrow = blocks.column_widths(['A', 'B'], [], 1.0, margin=0.0, title='x')\n" +
                "assert narrow == blocks.column_widths(['A', 'B'], [], 1.0, margin=0.0), narrow\n" +
                "try:\n" +
                "    blocks.column_widths(['A'], [], 0)\n" +
                "    raise AssertionError('zero height')\n" +
                "except ValueError:\n" +
                "    pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Ents_LengthByLayer_SumsAndCounts_SortedByLayer()
        {
            var r = Run(
                "from pyacad import ents\n" +
                "snaps = [{'layer': 'M-DUCT', 'length': 10.0, 'type': 'LINE'}, {'layer': 'm-pipe', 'length': 2.5},\n" +
                "         {'layer': 'M-DUCT', 'length': 5}, {'layer': 'A', 'length': None},\n" +
                "         {'layer': 'M-PIPE', 'length': 1.0}]\n" +
                "got = ents.length_by_layer(snaps)\n" +
                "assert got == [{'layer': 'M-DUCT', 'count': 2, 'length': 15.0},\n" +
                "               {'layer': 'M-PIPE', 'count': 1, 'length': 1.0},\n" +
                "               {'layer': 'm-pipe', 'count': 1, 'length': 2.5}], got\n" +
                "assert ents.length_by_layer([]) == []\n" +
                "assert 'SPLINE' in ents.LENGTH_TYPES and 'POLYLINE' in ents.LENGTH_TYPES\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }
    }
}
