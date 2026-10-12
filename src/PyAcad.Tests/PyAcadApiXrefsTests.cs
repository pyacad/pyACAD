using System;
using System.Collections.Generic;
using System.IO;
using PyAcad.Runtime.Engine;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// The pure half of pyacad.xrefs that the Xrefs panel (Xref Manager, Xref Repath)
    /// is built on: list labels, repath plans, the plan preview and the result summary.
    /// Run through the real IronPython engine; no AutoCAD and no filesystem, since
    /// plan_repath takes the file check as a callable.
    /// </summary>
    public class PyAcadApiXrefsTests
    {
        private readonly IronPythonEngine _engine;

        public PyAcadApiXrefsTests()
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

        // Rows shaped like xrefs.list() returns them; every test starts from these.
        private const string Rows =
            "from pyacad import xrefs\n" +
            "def row(name, path, status='loaded', nested=False, overlay=False):\n" +
            "    return {'name': name, 'path': path, 'found_at': '', 'status': status,\n" +
            "            'is_overlay': overlay, 'nested': nested}\n";

        [Fact]
        public void Describe_ShowsNameStatusAndPath_OnePerRow()
        {
            var r = Run(Rows +
                "rows = [row('Site', r'..\\Xref\\site.dwg'),\n" +
                "        row('Grid', r'C:\\P\\grid.dwg', status='not-found', overlay=True),\n" +
                "        row('Site|Survey', '', status='unloaded', nested=True)]\n" +
                "labels = xrefs.describe(rows)\n" +
                "assert labels == ['Site  [loaded]  ..\\\\Xref\\\\site.dwg',\n" +
                "                  'Grid  [not found, overlay]  C:\\\\P\\\\grid.dwg',\n" +
                "                  'Site|Survey  [unloaded, nested]  (no path)'], labels\n" +
                "assert xrefs.describe([]) == []\n" +
                "assert xrefs.describe([{'name': 'Bare'}]) == ['Bare  [unknown]  (no path)']\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void StatusLabel_SpellsOutNotFound()
        {
            var r = Run(
                "from pyacad import xrefs\n" +
                "assert [xrefs.status_label(s) for s in ('loaded', 'unloaded', 'not-found', 'unresolved', '', None)] == \\\n" +
                "    ['loaded', 'unloaded', 'not found', 'unresolved', 'unknown', 'unknown']\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PlanRepath_Relative_ChangesOnlyWhatCanBeRelative()
        {
            var r = Run(Rows +
                "host = r'C:\\Proj\\Host'\n" +
                "rows = [row('A', r'C:\\Proj\\Xref\\a.dwg'), row('B', r'.\\b.dwg'),\n" +
                "        row('C', r'D:\\Other\\c.dwg'), row('N', r'C:\\Proj\\n.dwg', nested=True),\n" +
                "        row('E', '')]\n" +
                "plan = xrefs.plan_repath(rows, 'relative', host_dir=host)\n" +
                "assert plan == [(r'C:\\Proj\\Xref\\a.dwg', r'..\\Xref\\a.dwg', ''),\n" +
                "                (r'.\\b.dwg', r'.\\b.dwg', 'already relative'),\n" +
                "                (r'D:\\Other\\c.dwg', r'D:\\Other\\c.dwg', 'on another drive or share'),\n" +
                "                (r'C:\\Proj\\n.dwg', r'C:\\Proj\\n.dwg', 'nested, follows its parent'),\n" +
                "                ('', '', 'no path')], plan\n" +
                "for host_dir in (None, ''):\n" +
                "    try:\n" +
                "        xrefs.plan_repath(rows, 'relative', host_dir=host_dir)\n" +
                "        raise AssertionError('no host folder accepted')\n" +
                "    except ValueError:\n" +
                "        pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PlanRepath_Absolute_ResolvesAgainstTheHostFolder()
        {
            var r = Run(Rows +
                "host = r'C:\\Proj\\Host'\n" +
                "rows = [row('B', r'.\\b.dwg'), row('A', r'C:\\Proj\\Xref\\a.dwg'),\n" +
                "        row('D', r'D:\\x\\.\\d.dwg'), row('U', r'..\\u.dwg', status='unloaded')]\n" +
                "plan = xrefs.plan_repath(rows, 'absolute', host_dir=host)\n" +
                "assert plan == [(r'.\\b.dwg', r'C:\\Proj\\Host\\b.dwg', ''),\n" +
                "                (r'C:\\Proj\\Xref\\a.dwg', r'C:\\Proj\\Xref\\a.dwg', 'already absolute'),\n" +
                "                (r'D:\\x\\.\\d.dwg', r'D:\\x\\d.dwg', ''),\n" +
                "                (r'..\\u.dwg', r'C:\\Proj\\u.dwg', '')], plan\n" +
                "try:\n" +
                "    xrefs.plan_repath(rows, 'absolute')\n" +
                "    raise AssertionError('no host folder accepted')\n" +
                "except ValueError:\n" +
                "    pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PlanRepath_Replace_IgnoresCase_AndNotesMisses()
        {
            var r = Run(Rows +
                "rows = [row('A', r'C:\\OLD\\a.dwg'), row('B', r'C:\\Keep\\b.dwg')]\n" +
                "plan = xrefs.plan_repath(rows, 'replace', find='old', replace='New')\n" +
                "assert plan == [(r'C:\\OLD\\a.dwg', r'C:\\New\\a.dwg', ''),\n" +
                "                (r'C:\\Keep\\b.dwg', r'C:\\Keep\\b.dwg', 'no match')], plan\n" +
                "plan = xrefs.plan_repath(rows, 'replace', find='OLD\\\\')\n" +
                "assert plan[0] == (r'C:\\OLD\\a.dwg', r'C:\\a.dwg', ''), plan\n" +
                "for find in (None, ''):\n" +
                "    try:\n" +
                "        xrefs.plan_repath(rows, 'replace', find=find, replace='x')\n" +
                "        raise AssertionError('empty find accepted')\n" +
                "    except ValueError:\n" +
                "        pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PlanRepath_Folder_RepathsOnlyFilesThatExistThere()
        {
            var r = Run(Rows +
                "asked = []\n" +
                "def exists(path):\n" +
                "    asked.append(path)\n" +
                "    return path.lower() in (r'e:\\new\\a.dwg', r'e:\\new\\c.dwg')\n" +
                "rows = [row('A', r'C:\\Old\\a.dwg'), row('B', r'..\\b.dwg'),\n" +
                "        row('C', r'e:\\NEW\\c.dwg'), row('N', r'C:\\Old\\n.dwg', nested=True)]\n" +
                "plan = xrefs.plan_repath(rows, 'folder', folder=r'E:\\New', exists=exists)\n" +
                "assert plan == [(r'C:\\Old\\a.dwg', r'E:\\New\\a.dwg', ''),\n" +
                "                (r'..\\b.dwg', r'..\\b.dwg', 'not found in the folder'),\n" +
                "                (r'e:\\NEW\\c.dwg', r'e:\\NEW\\c.dwg', 'already points there'),\n" +
                "                (r'C:\\Old\\n.dwg', r'C:\\Old\\n.dwg', 'nested, follows its parent')], plan\n" +
                "assert asked == [r'E:\\New\\a.dwg', r'E:\\New\\b.dwg'], asked\n" +
                "plan = xrefs.plan_repath(rows[:1], 'folder', folder='E:\\\\New\\\\', exists=exists)\n" +
                "assert plan == [(r'C:\\Old\\a.dwg', r'E:\\New\\a.dwg', '')], plan\n" +
                "for folder in (None, ''):\n" +
                "    try:\n" +
                "        xrefs.plan_repath(rows, 'folder', folder=folder, exists=exists)\n" +
                "        raise AssertionError('no folder accepted')\n" +
                "    except ValueError:\n" +
                "        pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PlanRepath_RejectsAnUnknownMode_AndPlansNothingForNoRows()
        {
            var r = Run(Rows +
                "assert xrefs.plan_repath([], 'replace', find='a') == []\n" +
                "try:\n" +
                "    xrefs.plan_repath([row('A', 'a.dwg')], 'sideways')\n" +
                "    raise AssertionError('unknown mode accepted')\n" +
                "except ValueError as e:\n" +
                "    assert 'sideways' in str(e), str(e)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void FormatPlan_ListsChangesThenWhatStays_AndCanBeCut()
        {
            var r = Run(Rows +
                "rows = [row('A', r'C:\\Old\\a.dwg'), row('B', r'C:\\x\\b.dwg'), row('C', r'C:\\Old\\c.dwg')]\n" +
                "plan = [(r'C:\\Old\\a.dwg', r'C:\\New\\a.dwg', ''),\n" +
                "        (r'C:\\x\\b.dwg', r'C:\\x\\b.dwg', 'no match'),\n" +
                "        (r'C:\\Old\\c.dwg', r'C:\\New\\c.dwg', '')]\n" +
                "text = xrefs.format_plan(rows, plan)\n" +
                "expected = '\\n'.join([\n" +
                "    'Changes to 2 xref paths:', '',\n" +
                "    'A', '  from  C:\\\\Old\\\\a.dwg', '  to    C:\\\\New\\\\a.dwg', '',\n" +
                "    'C', '  from  C:\\\\Old\\\\c.dwg', '  to    C:\\\\New\\\\c.dwg', '',\n" +
                "    'Left as they are:', '  B: no match'])\n" +
                "assert text == expected, repr(text)\n" +
                "cut = xrefs.format_plan(rows, plan, limit=1)\n" +
                "assert cut.startswith('Changes to 2 xref paths:\\n\\nA\\n'), repr(cut)\n" +
                "assert '\\n\\n...and 1 more\\n\\nLeft as they are:' in cut, repr(cut)\n" +
                "assert 'C:\\\\New\\\\c.dwg' not in cut, repr(cut)\n" +
                "one = xrefs.format_plan(rows[:1], plan[:1])\n" +
                "assert one.startswith('Changes to 1 xref path:'), repr(one)\n" +
                "assert 'Left as they are' not in one, repr(one)\n" +
                "none = xrefs.format_plan(rows[1:2], plan[1:2])\n" +
                "assert none == 'No xref path changes.\\n\\nLeft as they are:\\n  B: no match', repr(none)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Headline_AndSummarize_CountWhatWorked_AndListFailures()
        {
            var r = Run(
                "from pyacad import xrefs\n" +
                "results = [('A', None), ('B', 'not found at x.dwg'), ('C', '')]\n" +
                "assert xrefs.headline(results, 'Reloaded') == 'Reloaded 2 of 3 xrefs'\n" +
                "assert xrefs.headline([('A', None)], 'Bound') == 'Bound 1 xref'\n" +
                "assert xrefs.headline([('A', None), ('B', None)], 'Detached') == 'Detached 2 xrefs'\n" +
                "assert xrefs.headline([('A', 'boom')], 'Unloaded') == 'Unloaded 0 of 1 xref'\n" +
                "assert xrefs.headline([]) == 'No xrefs to update'\n" +
                "text = xrefs.summarize(results, 'Reloaded')\n" +
                "assert text == 'Reloaded 2 of 3 xrefs.\\n\\nNot done:\\n  B: not found at x.dwg', repr(text)\n" +
                "assert xrefs.summarize([('A', None)], 'Bound') == 'Bound 1 xref.'\n" +
                "assert xrefs.summarize([]) == 'No xrefs to update.'\n" +
                "many = [('X%d' % i, 'boom') for i in range(5)]\n" +
                "cut = xrefs.summarize(many, 'Unloaded', limit=2)\n" +
                "assert cut == 'Unloaded 0 of 5 xrefs.\\n\\nNot done:\\n  X0: boom\\n  X1: boom\\n  ...and 3 more', repr(cut)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void CheckLoaded_FailsWhatDidNotLoad_AndKeepsEarlierErrors()
        {
            var r = Run(Rows +
                "rows = [row('A', 'a.dwg'), row('B', r'..\\b.dwg', status='not-found'),\n" +
                "        row('C', 'c.dwg', status='unloaded')]\n" +
                "out = xrefs.check_loaded([('A', None), ('B', None), ('C', 'boom'), ('D', None)], rows)\n" +
                "assert out == [('A', None), ('B', r'not found at ..\\b.dwg'), ('C', 'boom'), ('D', None)], out\n" +
                "out = xrefs.check_loaded([('C', '')], rows)\n" +
                "assert out == [('C', 'unloaded at c.dwg')], out\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PureHelpers_ImportWithoutAutoCAD_AndLeaveListAlone()
        {
            // xrefs.list() shadows the builtin inside the module; the pure helpers must
            // not trip over that, and list() must still be the API call.
            var r = Run(
                "from pyacad import xrefs\n" +
                "for name in ('describe', 'status_label', 'plan_repath', 'format_plan',\n" +
                "             'headline', 'summarize', 'check_loaded'):\n" +
                "    assert callable(getattr(xrefs, name)), name\n" +
                "try:\n" +
                "    xrefs.list()\n" +
                "    raise AssertionError('no ImportError')\n" +
                "except ImportError as e:\n" +
                "    assert 'AutoCAD' in str(e), str(e)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }
    }
}
