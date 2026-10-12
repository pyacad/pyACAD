using System;
using System.Collections.Generic;
using System.IO;
using PyAcad.Runtime.Engine;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// Tests for the pyacad drawing API modules (doc, sel, ents, layers, blocks, xrefs,
    /// layouts, plot, audit, lisp, units), run through the real IronPython engine. Only
    /// the pure halves can run here; the API halves are pinned to import cleanly and to
    /// fail with the helpful ImportError from pyacad._api when called outside AutoCAD.
    /// </summary>
    public class PyAcadApiTests
    {
        public static readonly string[] ApiModules =
        {
            "doc", "sel", "ents", "layers", "blocks", "xrefs", "layouts", "plot", "audit", "lisp", "units",
        };

        private readonly IronPythonEngine _engine;

        public PyAcadApiTests()
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

        [Fact]
        public void EveryApiModule_ImportsWithoutAutoCAD()
        {
            foreach (var module in ApiModules)
            {
                var r = Run("import pyacad." + module + "\n");
                Assert.True(r.Succeeded, "pyacad." + module + " failed to import:\n" + r.ErrorText);
            }
        }

        [Theory]
        [InlineData("doc", "doc.active()")]
        [InlineData("sel", "sel.current()")]
        [InlineData("ents", "ents.get('2F3A')")]
        [InlineData("layers", "layers.names()")]
        [InlineData("blocks", "blocks.definitions()")]
        [InlineData("xrefs", "xrefs.list()")]
        [InlineData("layouts", "layouts.names()")]
        [InlineData("plot", "plot.devices()")]
        [InlineData("audit", "audit.proxies()")]
        [InlineData("lisp", "lisp.call('c:hello')")]
        [InlineData("units", "units.current()")]
        public void EveryApiFunction_RaisesHelpfulImportError_OutsideAutoCAD(string module, string call)
        {
            var r = Run(
                "from pyacad import " + module + "\n" +
                "try:\n" +
                "    " + call + "\n" +
                "    raise AssertionError('no ImportError')\n" +
                "except ImportError as e:\n" +
                "    assert 'AutoCAD' in str(e), str(e)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Doc_AsPointAsNamesAndStep_ArePure()
        {
            var r = Run(
                "from pyacad import doc\n" +
                "assert doc.as_point((1, 2)) == (1.0, 2.0, 0.0)\n" +
                "assert doc.as_point([1, 2, 3]) == (1.0, 2.0, 3.0)\n" +
                "for bad in ((1,), (1, 2, 3, 4), 'xy', None):\n" +
                "    try:\n" +
                "        doc.as_point(bad)\n" +
                "        raise AssertionError(bad)\n" +
                "    except ValueError:\n" +
                "        pass\n" +
                "assert doc.as_names('A') == ['A']\n" +
                "assert doc.as_names(('A', 'B')) == ['A', 'B']\n" +
                "assert doc.as_names(None) == []\n" +
                "seen = []\n" +
                "stops = [i for i in range(450) if doc.step(i, 450, lambda d, t: seen.append((d, t)), lambda: len(seen) == 3)]\n" +
                "assert seen == [(0, 450), (200, 450), (400, 450)], seen\n" +
                "assert stops == [400], stops\n" +
                "assert not doc.step(5, 10)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Sel_BuildFilter_MapsTypeLayerSpaceAndExtraCodes()
        {
            var r = Run(
                "from pyacad import sel\n" +
                "assert sel.build_filter() == []\n" +
                "assert sel.build_filter(type='line') == [(0, 'LINE')]\n" +
                "assert sel.build_filter(type=['line', ' Circle']) == [(0, 'LINE,CIRCLE')]\n" +
                "assert sel.build_filter(layer='A-*') == [(8, 'A-*')]\n" +
                "assert sel.build_filter(space='MODEL') == [(410, 'Model')]\n" +
                "assert sel.build_filter(space='Layout1') == [(410, 'Layout1')]\n" +
                "f = sel.build_filter(type='insert', layer='0', c66='1', c62=3, c40=2, c2='Door')\n" +
                "assert f == [(0, 'INSERT'), (8, '0'), (2, 'Door'), (40, 2.0), (62, 3), (66, 1)], f\n" +
                "assert sel.build_filter(c10=(1, 2)) == [(10, (1.0, 2.0, 0.0))]\n" +
                "for bad in ({'x62': 1}, {'c': 1}, {'cxy': 1}):\n" +
                "    try:\n" +
                "        sel.build_filter(**bad)\n" +
                "        raise AssertionError(bad)\n" +
                "    except ValueError:\n" +
                "        pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Sel_ValueKind_FollowsGroupCodeRanges()
        {
            var r = Run(
                "from pyacad import sel\n" +
                "expected = {0: 'text', 8: 'text', 410: 'text', 62: 'int16', 70: 'int16', 90: 'int32',\n" +
                "            420: 'int32', 40: 'double', 50: 'double', 10: 'point', 1010: 'point', 1071: 'int32'}\n" +
                "for code, kind in expected.items():\n" +
                "    assert sel.value_kind(code) == kind, (code, sel.value_kind(code))\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Ents_ResolveSpace_ReadsModelLayoutAndBlock()
        {
            var r = Run(
                "from pyacad import ents\n" +
                "assert ents.resolve_space(None) == ('model', 'Model')\n" +
                "assert ents.resolve_space('') == ('model', 'Model')\n" +
                "assert ents.resolve_space('MODEL') == ('model', 'Model')\n" +
                "assert ents.resolve_space('Layout1') == ('layout', 'Layout1')\n" +
                "assert ents.resolve_space('BLOCK:Door') == ('block', 'Door')\n" +
                "assert ents.resolve_space('block: Door 2') == ('block', 'Door 2')\n" +
                "try:\n" +
                "    ents.resolve_space('BLOCK:')\n" +
                "    raise AssertionError('empty block name')\n" +
                "except ValueError:\n" +
                "    pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Ents_HandleAndPropChecks_RejectBadInput()
        {
            var r = Run(
                "from pyacad import ents\n" +
                "assert ents.normalize_handle(' 2f3a ') == '2F3A'\n" +
                "assert ents.normalize_handle('0x1A') == '1A'\n" +
                "for bad in ('', 'XYZ', '12 3'):\n" +
                "    try:\n" +
                "        ents.normalize_handle(bad)\n" +
                "        raise AssertionError(bad)\n" +
                "    except ValueError:\n" +
                "        pass\n" +
                "ents.check_props('LINE', {'layer': 'A', 'color': 256, 'lineweight': 25, 'visible': False})\n" +
                "ents.check_props('MTEXT', {'text': 'x', 'height': 2.5})\n" +
                "ents.check_props('INSERT', {'attributes': {'TAG': 'v'}})\n" +
                "for type_name, props in (('LINE', {'text': 'x'}), ('LINE', {'color': 300}), ('LINE', {'color': True}),\n" +
                "                         ('LINE', {'lineweight': 7}), ('TEXT', {'height': 0}),\n" +
                "                         ('INSERT', {'attributes': ['x']}), ('CIRCLE', {'radius': 2})):\n" +
                "    try:\n" +
                "        ents.check_props(type_name, props)\n" +
                "        raise AssertionError((type_name, props))\n" +
                "    except ValueError:\n" +
                "        pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Layers_Matches_UsesStarAndQuestionMark_IgnoringCase()
        {
            var r = Run(
                "from pyacad import layers\n" +
                "assert layers.matches('A-WALL', 'a-*')\n" +
                "assert layers.matches('A-WALL', '*')\n" +
                "assert layers.matches('Wall01', 'wall??')\n" +
                "assert not layers.matches('Wall1', 'wall??')\n" +
                "assert not layers.matches('XA-WALL', 'A-*')\n" +
                "assert layers.matches('A.B', 'A?B')\n" +
                "assert not layers.matches('AxB', 'A.B')\n" +
                "assert layers.matches('[x]+', '[x]+')\n" +
                "assert layers.matches('Defpoints', 'DEFPOINTS')\n" +
                "assert layers.matches_any('E-LIGHT', ['A-*', 'e-*'])\n" +
                "assert not layers.matches_any('S-COL', ['A-*', 'E-*'])\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Xrefs_ApplyReplace_IgnoresCase()
        {
            var r = Run(
                "from pyacad import xrefs\n" +
                "assert xrefs.apply_replace(r'C:\\Old\\Proj\\a.dwg', 'old', 'New') == r'C:\\New\\Proj\\a.dwg'\n" +
                "assert xrefs.apply_replace(r'C:\\OLD\\old.dwg', 'Old', 'x') == r'C:\\x\\x.dwg'\n" +
                "assert xrefs.apply_replace(r'C:\\a.dwg', 'zzz', 'y') == r'C:\\a.dwg'\n" +
                "assert xrefs.apply_replace(r'C:\\a+b\\c.dwg', 'A+B', r'd\\1') == r'C:\\d\\1\\c.dwg'\n" +
                "try:\n" +
                "    xrefs.apply_replace('a', '', 'b')\n" +
                "    raise AssertionError('empty find')\n" +
                "except ValueError:\n" +
                "    pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Xrefs_MakeRelative_WalksUpAndKeepsOtherDrives()
        {
            var r = Run(
                "from pyacad import xrefs\n" +
                "host = r'C:\\Proj\\Host'\n" +
                "assert xrefs.make_relative(r'C:\\Proj\\Host\\a.dwg', host) == r'.\\a.dwg'\n" +
                "assert xrefs.make_relative(r'C:\\Proj\\Host\\xr\\a.dwg', host) == r'.\\xr\\a.dwg'\n" +
                "assert xrefs.make_relative(r'C:\\Proj\\Xref\\a.dwg', host) == r'..\\Xref\\a.dwg'\n" +
                "assert xrefs.make_relative(r'c:\\proj\\xref\\a.dwg', host) == r'..\\xref\\a.dwg'\n" +
                "assert xrefs.make_relative(r'D:\\Other\\a.dwg', host) == r'D:\\Other\\a.dwg'\n" +
                "assert xrefs.make_relative(r'\\\\server\\share\\a.dwg', host) == r'\\\\server\\share\\a.dwg'\n" +
                "assert xrefs.make_relative(r'..\\already.dwg', host) == r'..\\already.dwg'\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Xrefs_MakeAbsolute_ResolvesDotDot_AgainstHostFolder()
        {
            var r = Run(
                "from pyacad import xrefs\n" +
                "host = r'C:\\Proj\\Host'\n" +
                "assert xrefs.make_absolute(r'.\\a.dwg', host) == r'C:\\Proj\\Host\\a.dwg'\n" +
                "assert xrefs.make_absolute(r'..\\Xref\\a.dwg', host) == r'C:\\Proj\\Xref\\a.dwg'\n" +
                "assert xrefs.make_absolute(r'..\\..\\a.dwg', host) == r'C:\\a.dwg'\n" +
                "assert xrefs.make_absolute(r'D:\\x\\.\\a.dwg', host) == r'D:\\x\\a.dwg'\n" +
                "assert xrefs.make_absolute(r'a.dwg', '') == 'a.dwg'\n" +
                "for p in (r'C:\\Proj\\Xref\\a.dwg', r'C:\\Proj\\Host\\s\\b.dwg'):\n" +
                "    assert xrefs.make_absolute(xrefs.make_relative(p, host), host) == p\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Xrefs_StatusName_MapsEnumNames()
        {
            var r = Run(
                "from pyacad import xrefs\n" +
                "assert [xrefs.status_name(s) for s in ('Resolved', 'Unloaded', 'FileNotFound', 'Unresolved', 'Unreferenced')] == \\\n" +
                "    ['loaded', 'unloaded', 'not-found', 'unresolved', 'unreferenced']\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Layouts_IsModel_IgnoresCase()
        {
            var r = Run(
                "from pyacad import layouts\n" +
                "assert layouts.is_model('Model') and layouts.is_model('MODEL') and layouts.is_model(' model ')\n" +
                "assert not layouts.is_model('Layout1') and not layouts.is_model('Model2')\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Plot_OutputName_FillsTokens_AndCleansThem()
        {
            var r = Run(
                "from pyacad import plot\n" +
                "assert plot.output_name(r'C:\\out\\{drawing}-{layout}.pdf', r'C:\\d\\Plan A.dwg', 'A1') == r'C:\\out\\Plan A-A1.pdf'\n" +
                "assert plot.output_name('{layout}.pdf', 'x.dwg', 'A/B:C') == 'A_B_C.pdf'\n" +
                "assert plot.output_name('{drawing}_{drawing}.pdf', 'untitled', 'L') == 'untitled_untitled.pdf'\n" +
                "assert plot.output_name('fixed.pdf', 'x.dwg', 'L') == 'fixed.pdf'\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Plot_ScaleType_NamesStdScaleMembers()
        {
            var r = Run(
                "from pyacad import plot\n" +
                "assert plot.scale_type('fit') == 'ScaleToFit'\n" +
                "assert plot.scale_type('FIT') == 'ScaleToFit'\n" +
                "assert plot.scale_type('1:50') == 'StdScale1To50'\n" +
                "assert plot.scale_type('10 : 1') == 'StdScale10To1'\n" +
                "try:\n" +
                "    plot.scale_type('1:3')\n" +
                "    raise AssertionError('1:3')\n" +
                "except ValueError:\n" +
                "    pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Audit_GeometryKey_GroupsWithinTolerance()
        {
            var r = Run(
                "from pyacad import audit\n" +
                "line = {'type': 'LINE', 'layer': 'A', 'start': (0.0, 0.0, 0.0), 'end': (10.0, 5.0, 0.0)}\n" +
                "near = {'type': 'LINE', 'layer': 'a', 'start': (0.0, 0.0, 0.0), 'end': (10.0000000001, 5.0, 0.0)}\n" +
                "rev = {'type': 'LINE', 'layer': 'A', 'start': (10.0, 5.0, 0.0), 'end': (0.0, 0.0, 0.0)}\n" +
                "far = {'type': 'LINE', 'layer': 'A', 'start': (0.0, 0.0, 0.0), 'end': (10.01, 5.0, 0.0)}\n" +
                "other = dict(line, layer='B')\n" +
                "k = audit.geometry_key(line)\n" +
                "assert k == audit.geometry_key(near) == audit.geometry_key(rev)\n" +
                "assert k != audit.geometry_key(far)\n" +
                "assert k != audit.geometry_key(other)\n" +
                "assert audit.geometry_key(far, 0.1) == audit.geometry_key(line, 0.1)\n" +
                "c1 = {'type': 'CIRCLE', 'layer': '0', 'center': (1.0, 1.0, 0.0), 'radius': 2.0}\n" +
                "c2 = {'type': 'CIRCLE', 'layer': '0', 'center': (1.0, 1.0, 0.0), 'radius': 2.5}\n" +
                "assert audit.geometry_key(c1) != audit.geometry_key(c2)\n" +
                "p1 = {'type': 'LWPOLYLINE', 'layer': '0', 'points': [(0, 0), (1, 0)], 'closed': False}\n" +
                "p2 = dict(p1, closed=True)\n" +
                "assert audit.geometry_key(p1) != audit.geometry_key(p2)\n" +
                "hash(audit.geometry_key(p1))\n" +
                "assert audit.geometry_key({'type': 'HATCH', 'layer': '0'}) is None\n" +
                "try:\n" +
                "    audit.geometry_key(line, 0)\n" +
                "    raise AssertionError('tolerance 0')\n" +
                "except ValueError:\n" +
                "    pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Lisp_EncodeDecode_RoundTripsNestedValues()
        {
            var r = Run(
                "from pyacad import lisp\n" +
                "assert lisp.encode(None) == [(5019, None)]\n" +
                "assert lisp.encode(True) == [(5021, None)]\n" +
                "assert lisp.encode(False) == [(5019, None)]\n" +
                "assert lisp.encode(3) == [(5010, 3)]\n" +
                "assert lisp.encode(2.5) == [(5001, 2.5)]\n" +
                "assert lisp.encode('x') == [(5005, 'x')]\n" +
                "value = [1, 'a', None, True, [2.5, ['deep']]]\n" +
                "assert lisp.decode(lisp.encode(value)) == [value]\n" +
                "assert lisp.decode(lisp.encode({'k': 2})) == [[['k', 2]]]\n" +
                "assert lisp.decode([(5016, None), (5005, 'a'), (5010, 1), (5018, None)]) == [['a', 1]]\n" +
                "assert lisp.decode([(5009, (1.0, 2.0, 3.0)), (5014, None)]) == [(1.0, 2.0, 3.0)]\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Script_GetArgs_ReadsTheCallingScriptsArgs()
        {
            var withArgs = Run(
                "from pyacad import script, lisp\n" +
                "def nested():\n" +
                "    return script.get_args()\n" +
                "assert script.get_args() == ['a', 2], script.get_args()\n" +
                "assert nested() == ['a', 2]\n" +
                "assert lisp.args() == ['a', 2]\n",
                new Dictionary<string, object> { ["__args__"] = new List<object> { "a", 2 } });
            Assert.True(withArgs.Succeeded, withArgs.ErrorText);

            var without = Run(
                "from pyacad import script\n" +
                "assert script.get_args() == []\n");
            Assert.True(without.Succeeded, without.ErrorText);
        }

        [Fact]
        public void Script_GetDocumentPath_IsNoneOutsideAutoCAD()
        {
            var r = Run(
                "from pyacad import script\n" +
                "assert script.get_document_path() is None\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Units_ConvertAndFormat_IncludingFeetAndInches()
        {
            var r = Run(
                "from pyacad import units\n" +
                "assert units.UNITS[4][0] == 'Millimeters' and units.UNITS[1][0] == 'Inches'\n" +
                "assert sorted(units.UNITS) == list(range(25))\n" +
                "assert units.name_of(6) == 'Meters'\n" +
                "assert abs(units.to_metres(1, 'feet') - 0.3048) < 1e-12\n" +
                "assert abs(units.to_metres(250, 'Millimeters') - 0.25) < 1e-12\n" +
                "assert abs(units.from_metres(1, 'Inches') - 39.37007874015748) < 1e-9\n" +
                "assert abs(units.from_metres(units.to_metres(7.5, 'Yards'), 'yards') - 7.5) < 1e-12\n" +
                "assert units.format_length(30.5, 'Inches') == '2\\'-6.50\"', units.format_length(30.5, 'Inches')\n" +
                "assert units.format_length(2.5, 'Feet') == '2\\'-6.00\"'\n" +
                "assert units.format_length(11.999, 'Inches') == '1\\'-0.00\"'\n" +
                "assert units.format_length(-6, 'Inches', 0) == '-0\\'-6\"'\n" +
                "assert units.format_length(12.345, 'Millimeters') == '12.35 mm' or units.format_length(12.345, 'Millimeters') == '12.34 mm'\n" +
                "assert units.format_length(1.5, 'Meters', 3) == '1.500 m'\n" +
                "assert units.format_length(3, 'Undefined', 1) == '3.0'\n" +
                "for bad in (lambda: units.to_metres(1, 'parsec'), lambda: units.to_metres(1, 'Undefined'), lambda: units.name_of(99)):\n" +
                "    try:\n" +
                "        bad()\n" +
                "        raise AssertionError('accepted')\n" +
                "    except ValueError:\n" +
                "        pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }
    }
}
