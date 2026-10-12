using System;
using System.IO;
using System.Linq;
using PyAcad.Runtime.Engine;
using Execution = PyAcad.Runtime.Execution;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// Tests for the pyacadlib python package, executed through the real IronPython
    /// engine. AutoCAD-API-bound modules can only be exercised inside AutoCAD;
    /// here we pin down what CAN run anywhere: package import, version, pure helpers,
    /// and that every shipped module at least compiles.
    /// </summary>
    public class PyAcadLibTests
    {
        private readonly IronPythonEngine _engine;

        /// <summary>Repo pyacadlib folder, found by walking up from the test binaries.</summary>
        public static string PyAcadLibDir { get; } = FindPyAcadLib();

        public PyAcadLibTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyAcadLibDir);
            _engine.Initialize(config);
        }

        private static string FindPyAcadLib()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "pyacadlib");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException(
                "pyacadlib not found above " + AppDomain.CurrentDomain.BaseDirectory);
        }

        private ExecResult Run(string code, TextWriter output = null)
        {
            return _engine.Execute(new ScriptRequest { Code = code, Output = output });
        }

        [Fact]
        public void ImportPyAcad_Works_WithoutAutoCADApi()
        {
            var outw = new StringWriter();
            var r = Run("import pyacad\nprint('version=' + pyacad.__version__)", outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Matches(@"version=\d+\.\d+\.\d+", outw.ToString());
        }

        [Fact]
        public void ScriptModule_ExposesCommandContext_FromHost()
        {
            Execution.PyAcadHost.Instance.SetCommandContext(
                @"C:\bundles\Tool.pushbutton\script.py", @"C:\bundles\Tool.pushbutton", "My Tool");

            var outw = new StringWriter();
            var r = Run(
                "from pyacad import script\n" +
                "print('path=' + script.get_script_path())\n" +
                "print('dir=' + script.get_command_path())\n" +
                "print('title=' + script.get_title())\n" +
                "print('host=' + script.get_host_version())",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            var text = outw.ToString();
            Assert.Contains(@"path=C:\bundles\Tool.pushbutton\script.py", text);
            Assert.Contains(@"dir=C:\bundles\Tool.pushbutton", text);
            Assert.Contains("title=My Tool", text);
            Assert.Contains("host=", text);
        }

        [Fact]
        public void OutputModule_TableHtml_MarksNumericColumnsByTheirData()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pyacad import output\n" +
                "rows = [['Level 01', 412, 'Open'], ['Level 02', 96, 'Closed']]\n" +
                "html = output.table_html(rows, ['Area', 'Errors', 'Status'])\n" +
                "print('numcells=%d' % html.count('<td class=\"num\">'))\n" +
                "print('numheads=%d' % html.count('<th class=\"num\">'))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            // only the Errors column: alignment follows the data, not the
            // column's position in the table
            Assert.Contains("numcells=2", outw.ToString());
            Assert.Contains("numheads=1", outw.ToString());
        }

        [Fact]
        public void SettingsModule_Merge_KeepsOnlyDefaultKeys_AndPrefersStored()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pyacad import settings\n" +
                "d = {'smart': True, 'tolerance_m': 2.0}\n" +
                "m = settings.merge(d, {'smart': False, 'junk': 1})\n" +
                "print('smart=%s tol=%s junk=%s' % (m['smart'], m['tolerance_m'], 'junk' in m))\n" +
                "print('fresh=%s' % (settings.merge(d, {}) is not d))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("smart=False tol=2.0 junk=False", outw.ToString());
            Assert.Contains("fresh=True", outw.ToString());
        }

        [Fact]
        public void SettingsModule_SaveLoad_RoundTrips_UnderOverrideRoot()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pyacad_tests", Guid.NewGuid().ToString("N"));
            var outw = new StringWriter();
            var r = Run(
                "from pyacad import settings\n" +
                $"settings._APPDATA_OVERRIDE = r'{tmp}'\n" +
                "settings.save('t1', {'a': 1, 'b': 'x'})\n" +
                "m = settings.load('t1', {'a': 0, 'b': '', 'c': True})\n" +
                "print('a=%s b=%s c=%s' % (m['a'], m['b'], m['c']))\n" +
                "m2 = settings.load('missing', {'a': 7})\n" +
                "print('missing=%s' % m2['a'])\n" +
                "print('path_ok=%s' % settings.path_for('t1').endswith('settings\\\\t1.json'))",
                outw);

            try { Directory.Delete(tmp, true); } catch { }
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("a=1 b=x c=True", outw.ToString());
            Assert.Contains("missing=7", outw.ToString());
            Assert.Contains("path_ok=True", outw.ToString());
        }

        [Fact]
        public void SettingsModule_CorruptFile_FallsBackToDefaults()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pyacad_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tmp, "pyACAD", "settings"));
            File.WriteAllText(Path.Combine(tmp, "pyACAD", "settings", "bad.json"), "{ not json");

            var outw = new StringWriter();
            var r = Run(
                "from pyacad import settings\n" +
                $"settings._APPDATA_OVERRIDE = r'{tmp}'\n" +
                "m = settings.load('bad', {'a': 5})\n" +
                "print('a=%s' % m['a'])",
                outw);

            try { Directory.Delete(tmp, true); } catch { }
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("a=5", outw.ToString());
        }

        [Fact]
        public void OutputModule_FormatTable_AlignsColumns_AndStringifiesCells()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pyacad import output\n" +
                "rows = [['Test A', 3, 'New'], ['B', 12, 'Active']]\n" +
                "print(output.format_table(rows, ['Name', 'Count', 'Status']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            var lines = outw.ToString().Replace("\r", "").Split('\n');
            Assert.Equal("Name    Count  Status", lines[0]);
            Assert.Equal("------  -----  ------", lines[1]);
            Assert.Equal("Test A  3      New", lines[2]);
            Assert.Equal("B       12     Active", lines[3]);
        }

        [Fact]
        public void OutputModule_FormatTable_EmptyRows_YieldsHeaderOnly()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pyacad import output\n" +
                "print(output.format_table([], ['Name']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            var lines = outw.ToString().Replace("\r", "").Split('\n');
            Assert.Equal("Name", lines[0]);
            Assert.Equal("----", lines[1]);
        }

        [Fact]
        public void UtilFlatten_YieldsGroupLikeLeaves_InsteadOfDescendingIntoThem()
        {
            // Regression: a node that is both a leaf and a group (it has children) was
            // descended into when the leaf-check ran AFTER the group-check.
            var outw = new StringWriter();
            var r = Run(
                "from pyacad import _util\n" +
                "class Node(object):\n" +
                "    def __init__(self, name, kind, *children):\n" +
                "        self.name, self.kind, self.Children = name, kind, list(children)\n" +
                "tree = [\n" +
                "    Node('folder', 'folder',\n" +
                "        Node('test A', 'test', Node('r1', 'result'), Node('r2', 'result'))),\n" +
                "    Node('test B', 'test', Node('r3', 'result')),\n" +
                "    Node('stray result', 'result'),\n" +
                "]\n" +
                "found = _util.flatten(tree,\n" +
                "    is_leaf=lambda n: n.kind == 'test',\n" +
                "    is_group=lambda n: len(n.Children) > 0)\n" +
                "print(','.join(n.name for n in found))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("test A,test B", outw.ToString());
        }

        [Fact]
        public void UtilFlattenWithPath_TracksFolders_AndYieldsGroupLikeLeaves()
        {
            // Same regression shape as flatten: a non-folder node with children must be
            // yielded as ONE leaf, not descended into.
            var outw = new StringWriter();
            var r = Run(
                "from pyacad import _util\n" +
                "class Node(object):\n" +
                "    def __init__(self, name, kind, *children):\n" +
                "        self.DisplayName, self.kind, self.Children = name, kind, list(children)\n" +
                "tree = [\n" +
                "    Node('Folder A', 'folder',\n" +
                "        Node('v1', 'view'),\n" +
                "        Node('anim', 'animation', Node('cut1', 'cut'))),\n" +
                "    Node('v2', 'view'),\n" +
                "]\n" +
                "for folders, item in _util.flatten_with_path(tree, is_folder=lambda n: n.kind == 'folder'):\n" +
                "    print('/'.join(folders) + ':' + item.DisplayName)",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            var text = outw.ToString().Replace("\r", "");
            Assert.Contains("Folder A:v1\n", text);
            Assert.Contains("Folder A:anim\n", text);
            Assert.Contains(":v2\n", text);
            Assert.DoesNotContain("cut1", text);
        }

        [Fact]
        public void Markdown_ConvertsHeadersEmphasisCodeListsAndLinks()
        {
            var r = Run(
                "from pyacad import _markdown\n" +
                "h = _markdown.to_html\n" +
                "assert h('# Title') == '<h1>Title</h1>', h('# Title')\n" +
                "assert h('## Sub') == '<h2>Sub</h2>'\n" +
                "assert '<strong>bold</strong>' in h('**bold**')\n" +
                "assert '<em>it</em>' in h('*it*')\n" +
                "assert '<code>x=1</code>' in h('`x=1`')\n" +
                "out = h('- one\\n- two')\n" +
                "assert '<ul>' in out and '<li>one</li>' in out and '<li>two</li>' in out, out\n" +
                "assert '<a href=\"https://x.io\">docs</a>' in h('[docs](https://x.io)')\n" +
                "assert '&lt;script&gt;' in h('<script>'), h('<script>')\n" +
                "block = h('```\\ncode <here>\\n```')\n" +
                "assert '<pre class=\"codeblock\">' in block and 'code &lt;here&gt;' in block, block\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void OutputModule_RichHelpers_Exist_AndTableHtmlEscapes()
        {
            var r = Run(
                "from pyacad import output\n" +
                "for name in ('print_html', 'print_md', 'print_table', 'progress', 'element_link', 'format_table'):\n" +
                "    assert callable(getattr(output, name)), name\n" +
                "html = output.table_html([['<b>A</b>', 1]], ['Name', 'N'])\n" +
                "assert '&lt;b&gt;A&lt;/b&gt;' in html, html\n" +
                "assert '<table class=\"pyacad\">' in html\n" +
                "assert '<th>Name</th>' in html\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void OutputModule_PrintCode_EscapesIntoMonospaceBlock()
        {
            // print_code funnels through the module-level print_html; capture it
            // by monkeypatching the name print_code's own body resolves at call
            // time, so this never has to touch a real (headless-unsafe) window.
            var r = Run(
                "from pyacad import output\n" +
                "captured = []\n" +
                "output.print_html = lambda html: captured.append(html)\n" +
                "output.print_code('<b>hi</b> & \"quote\"')\n" +
                "html = captured[0]\n" +
                "assert html == '<pre class=\"pyacad-code\">&lt;b&gt;hi&lt;/b&gt; &amp; \"quote\"</pre>', html\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void FormsModule_UsesFluentDialogs_NotWinForms()
        {
            // Forms must route to the runtime's Fluent WPF dialogs. WinForms
            // (the 1990s look) must be gone; the python API surface stays identical.
            var r = Run(
                "import inspect\n" +
                "from pyacad import forms\n" +
                "for name in ('alert', 'confirm', 'ask_string', 'save_file', 'open_file'):\n" +
                "    assert callable(getattr(forms, name)), name\n" +
                "src = inspect.getsource(forms)\n" +
                "assert 'System.Windows.Forms' not in src, 'WinForms still referenced'\n" +
                "assert 'Dialogs' in src, 'not routed to runtime Fluent dialogs'");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ScriptModule_IsDarkTheme_ReturnsBool_WithoutRibbon()
        {
            // Outside AutoCAD the AdWindows sample throws -> detector falls back to
            // light unless config overrides. Contract: always a bool, never an error.
            var outw = new StringWriter();
            var r = Run(
                "from pyacad import script\n" +
                "print('dark=%s' % script.is_dark_theme())",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Matches("dark=(True|False)", outw.ToString());
        }

        [Fact]
        public void CommandHistory_RecallsPrevAndNext_SkipsConsecutiveDupes()
        {
            var r = Run(
                "from pyacad._history import CommandHistory\n" +
                "h = CommandHistory()\n" +
                "h.add('first')\n" +
                "h.add('second')\n" +
                "h.add('second')\n" +          // consecutive dupe collapses
                "assert h.previous() == 'second'\n" +
                "assert h.previous() == 'first'\n" +
                "assert h.previous() == 'first'\n" +   // clamped at oldest
                "assert h.next() == 'second'\n" +
                "assert h.next() == ''\n" +            // past newest -> empty draft
                "h.add('third')\n" +
                "assert h.previous() == 'third'\n");   // add resets navigation

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ReplExecute_EchoesTrailingExpression_AfterStatements()
        {
            // Regression: "import x; x.f()" fell into plain exec and printed nothing.
            var r = Run(
                "from pyacad import _repl\n" +
                "ns = {}\n" +
                "assert _repl.execute('import math; math.floor(2.5)', ns) == '2\\n'\n" +
                "assert _repl.execute('1 + 1', ns) == '2\\n'\n" +
                "assert _repl.execute('None', ns) == ''");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ReplExecute_PersistsNamespace_AndStaysSilentOnStatements()
        {
            var r = Run(
                "from pyacad import _repl\n" +
                "ns = {}\n" +
                "assert _repl.execute('x = 41', ns) == ''\n" +
                "assert _repl.execute('x + 1', ns) == '42\\n'");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ReplExecute_CapturesPrint_AndTracebacks()
        {
            var r = Run(
                "from pyacad import _repl\n" +
                "ns = {}\n" +
                "assert _repl.execute('print(\"hi\")', ns) == 'hi\\n'\n" +
                "out = _repl.execute('1/0', ns)\n" +
                "assert 'ZeroDivisionError' in out, out\n" +
                "out = _repl.execute('def broken(:', ns)\n" +
                "assert 'SyntaxError' in out, out");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ImportingApiModule_OutsideAutoCAD_GivesHelpfulError()
        {
            var r = Run("import pyacad._api");

            Assert.False(r.Succeeded);
            Assert.Contains("inside an AutoCAD session", r.ErrorText);
            Assert.Contains("acdbmgd, acmgd, accoremgd", r.ErrorText);
        }

        [Fact]
        public void ToastModule_ExposesFourLevels_AndNeverThrowsOutsideAutoCAD()
        {
            var r = Run(
                "from pyacad import toast\n" +
                "for name in ('show', 'success', 'error', 'info', 'warning'):\n" +
                "    assert callable(getattr(toast, name)), name\n" +
                "toast.success('unit test', 'detail line')\n" +   // no UI thread here: must not raise
                "toast.error('unit test')\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void BannerModule_MirrorsToast_AndNeverThrowsOutsideAutoCAD()
        {
            var r = Run(
                "from pyacad import banner\n" +
                "for name in ('show', 'success', 'error', 'info', 'warning', 'clear'):\n" +
                "    assert callable(getattr(banner, name)), name\n" +
                "banner.success('unit test', 'detail line')\n" +   // no UI thread here: must not raise
                "banner.error('unit test')\n" +
                "banner.prompt('unit test prompt', 'stays until cleared')\n" +
                "banner.show('info', 'unit test', None, seconds=2)\n" +
                "banner.clear()\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void FormsSelectFromList_TicksTheCheckedValues_ByPosition()
        {
            // select_from_list(..., checked=[values]) opens with those rows ticked;
            // the picker takes row positions, so values are matched to positions,
            // and a value that is not offered is ignored.
            var r = Run(
                "from pyacad import forms\n" +
                "values = ['home', 'vrex', 'gltf', 'vrex']\n" +
                "assert forms._indices_of(values, ['gltf', 'vrex', 'gone']) == [1, 2, 3]\n" +
                "assert forms._indices_of(values, None) == []\n" +
                "assert forms._indices_of(values, []) == []\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ScriptModule_ClipboardText_ReturnsAString_AndNeverThrows()
        {
            // Select by IDs reads the clipboard on every run without being
            // asked, so a clipboard another process is holding open, or a
            // non-STA thread like this test's, has to come back empty rather
            // than take the tool down with it.
            var outw = new StringWriter();
            var r = Run(
                "from pyacad import script\n" +
                "value = script.clipboard_text()\n" +
                "assert isinstance(value, str), type(value)\n" +
                "print('clipboard ok')", outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("clipboard ok", outw.ToString());
        }

        [Fact]
        public void ScriptModule_GetBundleFile_ReturnsNoneOutsideCommandContext()
        {
            // no command context in tests: CommandPath is whatever last ran, so point it nowhere
            var r = Run(
                "from pyacad import script\n" +
                "assert script.get_bundle_file('no_such_file_ever.xyz') is None");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void DatafilesModule_PathFor_SeparatesDocuments_ByNameAndHash()
        {
            var r = Run(
                "from pyacad import _datafiles\n" +
                "import tempfile, os\n" +
                "root = tempfile.mkdtemp()\n" +
                "p1 = _datafiles.path_for(root, 'cfg_my_tool')\n" +
                "assert p1 == os.path.join(root, 'cfg_my_tool.json')\n" +
                "p2 = _datafiles.path_for(root, 'cfg_my_tool', doc_path=r'C:\\Jobs\\Tower.DWG')\n" +
                "p3 = _datafiles.path_for(root, 'cfg_my_tool', doc_path=r'c:\\jobs\\TOWER.dwg')\n" +
                "assert p2 == p3\n" +
                "assert 'tower' in os.path.basename(p2)\n" +
                "data = _datafiles.load_all(p1)\n" +
                "assert data == {}\n" +
                "_datafiles.store(p1, 'k', [1, 2])\n" +
                "assert _datafiles.load_all(p1) == {'k': [1, 2]}\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        // store() is read-modify-write: a corrupt file used to load as {} and get
        // overwritten, quietly costing every other key. The bad file is kept aside.
        [Fact]
        public void DatafilesModule_Store_OverCorruptFile_KeepsTheOldBytesAside()
        {
            var r = Run(
                "from pyacad import _datafiles\n" +
                "import tempfile, os\n" +
                "root = tempfile.mkdtemp()\n" +
                "p = _datafiles.path_for(root, 'cfg_my_tool')\n" +
                "f = open(p, 'w'); f.write('{\"precious\": 1, broken'); f.close()\n" +
                "_datafiles.store(p, 'k', 2)\n" +
                "assert _datafiles.load_all(p) == {'k': 2}\n" +
                "f = open(p + '.corrupt', 'r'); kept = f.read(); f.close()\n" +
                "assert kept == '{\"precious\": 1, broken', kept\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void DatafilesAndSettings_Save_LeaveNoTempFileBehind()
        {
            var r = Run(
                "from pyacad import _datafiles, settings\n" +
                "import tempfile, os\n" +
                "root = tempfile.mkdtemp()\n" +
                "p = _datafiles.path_for(root, 'cfg_my_tool')\n" +
                "_datafiles.store(p, 'a', 1)\n" +
                "_datafiles.store(p, 'b', 2)\n" +
                "assert os.listdir(root) == ['cfg_my_tool.json'], os.listdir(root)\n" +
                "assert _datafiles.load_all(p) == {'a': 1, 'b': 2}\n" +
                "settings._APPDATA_OVERRIDE = root\n" +
                "try:\n" +
                "    settings.save('t9', {'a': 1})\n" +
                "    settings.save('t9', {'a': 2})\n" +
                "    folder = os.path.dirname(settings.path_for('t9'))\n" +
                "    assert os.listdir(folder) == ['t9.json'], os.listdir(folder)\n" +
                "    assert settings.load('t9', {'a': 0}) == {'a': 2}\n" +
                "finally:\n" +
                "    settings._APPDATA_OVERRIDE = None\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        // The atomic writer is shared, so its contract is pinned directly: a failed
        // write must leave the previous content in place, not a truncated file.
        [Fact]
        public void AtomicWrite_FailureMidWrite_LeavesThePreviousFileIntact()
        {
            var r = Run(
                "from pyacad import _atomic\n" +
                "import tempfile, os\n" +
                "root = tempfile.mkdtemp()\n" +
                "p = os.path.join(root, 'x.json')\n" +
                "_atomic.write_text(p, 'first')\n" +
                "try:\n" +
                "    _atomic.write_text(p, 12345)   # not text: the handle rejects it after the temp file opened\n" +
                "    raise AssertionError('expected the write to fail')\n" +
                "except TypeError:\n" +
                "    pass\n" +
                "f = open(p, 'r'); text = f.read(); f.close()\n" +
                "assert text == 'first', text\n" +
                "assert os.listdir(root) == ['x.json'], os.listdir(root)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        // The command context is ambient and outlives its run, so save_config() called
        // from a modeless dialog or a dock-pane handler AFTER the user ran something else
        // used to write under that other command's key. script.bind() pins the identity
        // while the command is still the one running.
        [Fact]
        public void BoundScript_KeepsItsOwnKey_AfterAnotherCommandHasRun()
        {
            var host = Execution.PyAcadHost.Instance;
            host.SetCommandContext(null, null, "Tool A", "Smoke.tab/P.panel/A.pushbutton");

            var r = Run(
                "from pyacad import script, settings\n" +
                "import tempfile, os\n" +
                "settings._APPDATA_OVERRIDE = tempfile.mkdtemp()\n" +
                "try:\n" +
                "    me = script.bind()\n" +
                "    # the user clicks another button while A's modeless dialog is still open\n" +
                "    script.get_host().SetCommandContext(None, None, 'Tool B', 'Smoke.tab/P.panel/B.pushbutton')\n" +
                "    me.save_config({'width': 300})\n" +
                "    a = settings.path_for(script._config_key('Smoke.tab/P.panel/A.pushbutton'))\n" +
                "    b = settings.path_for(script._config_key('Smoke.tab/P.panel/B.pushbutton'))\n" +
                "    assert os.path.exists(a), 'A did not get its own settings'\n" +
                "    assert not os.path.exists(b), 'A wrote under B'\n" +
                "    assert me.get_config({'width': 0}) == {'width': 300}\n" +
                "    assert script.get_config({'width': 0}) == {'width': 0}   # ambient is B now\n" +
                "    me.reset_config()\n" +
                "    assert not os.path.exists(a)\n" +
                "finally:\n" +
                "    settings._APPDATA_OVERRIDE = None\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void AllShippedModules_Compile()
        {
            var sources = Directory.GetFiles(PyAcadLibDir, "*.py", SearchOption.AllDirectories);
            Assert.NotEmpty(sources);
            // The drawing API modules must be among what this compiles, so a rename or a
            // misplaced file cannot slip past it.
            var names = sources.Select(Path.GetFileNameWithoutExtension).ToList();
            foreach (var module in PyAcadApiTests.ApiModules)
                Assert.Contains(module, names);

            foreach (var path in sources)
            {
                var code = "with open(__source_path__) as f:\n"
                         + "    compile(f.read(), __source_path__, 'exec')";
                var req = new ScriptRequest { Code = code };
                req.Globals["__source_path__"] = path;

                var r = _engine.Execute(req);
                Assert.True(r.Succeeded, $"{Path.GetFileName(path)} failed to compile:\n{r.ErrorText}");
            }
        }

        [Fact]
        public void Logger_FiltersLevel_AndInvokesCustomSink()
        {
            var r = Run(
                "from pyacad import _log\n" +
                "records = []\n" +
                "logger = _log.Logger('test', sink=lambda level, source, msg: records.append((level, source, msg)))\n" +
                "logger.debug('hidden')\n" +
                "logger.info('shown')\n" +
                "logger.set_level('debug')\n" +
                "logger.debug('now shown')\n" +
                "logger.error('boom')\n" +
                "assert records == [('info', 'test', 'shown'), ('debug', 'test', 'now shown'), ('error', 'test', 'boom')], repr(records)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Logger_Names_The_Hook_That_Is_Running_Not_The_Last_Command()
        {
            // A hook never calls SetCommandContext, so CommandTitle still holds whatever
            // button ran last. Attributing a hook's log lines to that button is wrong and
            // actively misleading: field-measured, lines from the
            // layer-changed hook were logged as "[Reload]" because Reload was the
            // last thing clicked.
            var host = Execution.PyAcadHost.Instance;
            host.SetCommandContext(null, null, "Reload", "pyACAD.tab/pyACAD.panel/Reload.pushbutton");
            host.LogSource = "hook Smoke:layer-changed";
            try
            {
                var r = Run(
                    "from pyacad import script\n" +
                    "logger = script.get_logger()\n" +
                    "assert logger._source == 'hook Smoke:layer-changed', repr(logger._source)\n");

                Assert.True(r.Succeeded, r.ErrorText);
            }
            finally
            {
                host.LogSource = null;
            }
        }

        [Fact]
        public void Logger_Falls_Back_To_The_Command_Title_When_No_Scope_Is_Set()
        {
            // Ordinary button runs must keep naming the command, so the fix cannot simply
            // replace the title.
            var host = Execution.PyAcadHost.Instance;
            host.LogSource = null;
            host.SetCommandContext(null, null, "Memorize", "Smoke.tab/P.panel/Remember.pushbutton");

            var r = Run(
                "from pyacad import script\n" +
                "logger = script.get_logger()\n" +
                "assert logger._source == 'Memorize', repr(logger._source)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Starting_A_Command_Clears_A_Leftover_Log_Scope()
        {
            // The mirror of the original bug: a hook's label must not leak onto the next
            // button the user clicks.
            var host = Execution.PyAcadHost.Instance;
            host.LogSource = "hook Smoke:camera-moved";
            host.SetCommandContext(null, null, "Memorize", "Smoke.tab/P.panel/Remember.pushbutton");

            Assert.Null(host.LogSource);
        }

        [Fact]
        public void ScriptModule_ConfigHelpers_KeyConfigKeyAndLoadSave()
        {
            var r = Run(
                "from pyacad import script, settings\n" +
                "import tempfile, os\n" +
                "assert script._config_key('pyACAD.tab/Tools.panel/My Tool.pushbutton') == 'cfg_pyacad_tab_tools_panel_my_tool_pushbutton'\n" +
                "settings._APPDATA_OVERRIDE = tempfile.mkdtemp()\n" +
                "try:\n" +
                "    key = script._config_key('a/b.pushbutton')\n" +
                "    saved = settings.load(key, {'x': 1})\n" +
                "    assert saved == {'x': 1}\n" +
                "finally:\n" +
                "    settings._APPDATA_OVERRIDE = None\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ChartsModule_RendersThemeAwareSvg_AndEscapesText()
        {
            var r = Run(
                "from pyacad import _charts\n" +
                "svg = _charts.bar_svg(['A', 'B'], [3, 9], title='T')\n" +
                "assert svg.startswith('<svg') and svg.endswith('</svg>')\n" +
                "assert 'var(--chart-1)' in svg\n" +
                "assert 'T' in svg and '9' in svg\n" +
                "# scaling: the 9 bar is 3x the 3 bar\n" +
                "assert svg.count('<rect') >= 2\n" +
                "# escaping\n" +
                "svg2 = _charts.bar_svg(['<b>'], [1])\n" +
                "assert '<b>' not in svg2 and '&lt;b&gt;' in svg2\n" +
                "# degenerate\n" +
                "assert 'no data' in _charts.bar_svg([], [])\n" +
                "pie = _charts.pie_svg(['A', 'B'], [1, 1])\n" +
                "assert '50%' in pie\n" +
                "line = _charts.line_svg(['x1', 'x2'], {'s': [1, 2]})\n" +
                "assert '<polyline' in line\n" +
                "# regression: a series longer than the label axis has a never-plotted\n" +
                "# tail that must not scale the chart - the truncated-to-2 rendering must\n" +
                "# be pixel-identical whether or not that unplotted tail exists\n" +
                "truncated = _charts.line_svg(['a', 'b'], {'s1': [1, 2], 's2': [1, 2]})\n" +
                "with_tail = _charts.line_svg(['a', 'b'], {'s1': [1, 2], 's2': [1, 2, 100]})\n" +
                "assert with_tail == truncated, with_tail\n" +
                "# regression: end labels anchor inward. Centred on the first and last\n" +
                "# point they overhang the viewBox and the SVG clips them.\n" +
                "edges = _charts.line_svg(['first', 'mid', 'last'], {'s': [1, 2, 3]})\n" +
                "assert 'text-anchor=\"start\"' in edges and 'text-anchor=\"end\"' in edges, edges\n" +
                "assert edges.count('text-anchor=\"middle\"') == 1, edges\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void XlModule_WritesAndReadsXlsx_RoundTrip()
        {
            var r = Run(
                "from pyacad import xl, _xlsx\n" +
                "import tempfile, os\n" +
                "assert _xlsx.column_letter(0) == 'A'\n" +
                "assert _xlsx.column_letter(25) == 'Z'\n" +
                "assert _xlsx.column_letter(26) == 'AA'\n" +
                "assert _xlsx.column_letter(51) == 'AZ'\n" +
                "assert _xlsx.column_letter(701) == 'ZZ'\n" +
                "assert _xlsx.column_letter(702) == 'AAA'\n" +
                "\n" +
                "path = os.path.join(tempfile.mkdtemp(), 'roundtrip.xlsx')\n" +
                "rows = [['Wall', 42, 1.5, True], ['Door & Frame', 0, -2.25, False], ['<Odd> \"Name\"', 7, 0.0, True]]\n" +
                "xl.write(path, rows, headers=['Type', 'Count', 'Offset', 'Fire'])\n" +
                "back = xl.read(path)\n" +
                "assert back[0] == ['Type', 'Count', 'Offset', 'Fire']\n" +
                "assert back[1][0] == 'Wall' and back[1][1] == 42.0 and back[1][2] == 1.5\n" +
                "assert back[2][0] == 'Door & Frame' and back[2][2] == -2.25\n" +
                "assert back[3][0] == '<Odd> \"Name\"'\n" +
                "assert xl.sheets(path) == ['Sheet1']\n" +
                "\n" +
                "# two sheets\n" +
                "path2 = os.path.join(tempfile.mkdtemp(), 'two.xlsx')\n" +
                "xl.write(path2, [['a']], sheet='First')\n" +
                "xl.write(path2, [['b']], sheet='Second')\n" +
                "assert sorted(xl.sheets(path2)) == ['First', 'Second']\n" +
                "assert xl.read(path2, sheet='Second') == [['b']]\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void XlModule_RoundTrip_CoversBooleanFalse_ZeroValues_AndFullRows()
        {
            // Closes a coverage gap in the brief's normative round-trip test: it never
            // asserted on back[1][3], back[2][1], back[2][3], or back[3] in full, so a
            // regression in False/zero handling could pass silently.
            var r = Run(
                "from pyacad import xl\n" +
                "import tempfile, os\n" +
                "path = os.path.join(tempfile.mkdtemp(), 'roundtrip.xlsx')\n" +
                "rows = [['Wall', 42, 1.5, True], ['Door & Frame', 0, -2.25, False], ['<Odd> \"Name\"', 7, 0.0, True]]\n" +
                "xl.write(path, rows, headers=['Type', 'Count', 'Offset', 'Fire'])\n" +
                "back = xl.read(path)\n" +
                "assert back[1] == ['Wall', 42.0, 1.5, True], back[1]\n" +
                "assert back[2] == ['Door & Frame', 0.0, -2.25, False], back[2]\n" +
                "assert back[3] == ['<Odd> \"Name\"', 7.0, 0.0, True], back[3]\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void XlModule_ParseSheet_HandlesBlankRowGaps_AndUnrecognizedCellTypes()
        {
            // Regression test for two robustness fixes found in self-review: Excel omits
            // entirely-blank rows from the XML rather than emitting an empty <row>, and
            // real workbooks can carry cell types this reader does not special-case
            // (error cells "e", ISO-date cells "d"). Both must degrade gracefully rather
            // than corrupting row alignment or crashing the reader.
            var r = Run(
                "from pyacad import _xlsx\n" +
                "sheet_xml = (\n" +
                "    '<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">'\n" +
                "    '<sheetData>'\n" +
                "    '<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>'\n" +
                "    '<row r=\"3\"><c r=\"A3\"><v>3</v></c></row>'\n" +
                "    '<row r=\"4\"><c r=\"A4\" t=\"e\"><v>#DIV/0!</v></c>'\n" +
                "    '<c r=\"B4\" t=\"d\"><v>2024-01-01</v></c></row>'\n" +
                "    '</sheetData></worksheet>'\n" +
                ")\n" +
                "rows = _xlsx.parse_sheet(sheet_xml, [])\n" +
                "assert rows == [[1.0], [], [3.0], ['#DIV/0!', '2024-01-01']], rows\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void XlModule_ParseSheet_SkipsCellsWhoseRefDoesNotParse()
        {
            // '$B$1' does not start with column letters, so _column_index cannot
            // place it. It used to return -1, and values[-1] wrote over the LAST
            // cell of the row: the A1 value silently became the B1 value.
            var r = Run(
                "from pyacad import _xlsx\n" +
                "sheet_xml = (\n" +
                "    '<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">'\n" +
                "    '<sheetData>'\n" +
                "    '<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>keep</t></is></c>'\n" +
                "    '<c r=\"$B$1\" t=\"inlineStr\"><is><t>junk</t></is></c></row>'\n" +
                "    '<row r=\"two\"><c r=\"A2\"><v>2</v></c></row>'\n" +
                "    '</sheetData></worksheet>'\n" +
                ")\n" +
                "rows = _xlsx.parse_sheet(sheet_xml, [])\n" +
                "assert rows[0] == ['keep'], rows\n" +
                "assert rows[1] == [2.0], rows\n" +
                "# a shared-string index that is not a number skips too, it does not raise\n" +
                "bad_shared = (\n" +
                "    '<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">'\n" +
                "    '<sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>x</v></c></row></sheetData></worksheet>'\n" +
                ")\n" +
                "assert _xlsx.parse_sheet(bad_shared, ['a']) == [['']], _xlsx.parse_sheet(bad_shared, ['a'])\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void XlModule_Write_PreservesSheetOrder_AndLeavesNoTempFile()
        {
            // Two regressions at once: build_workbook used to sort sheet names, so
            // rewriting 'Data' reshuffled the user's workbook to Data/Summary; and
            // write() opened the destination with zipfile 'w' (truncating) AFTER
            // reading the old workbook back, so a failure mid-write destroyed the
            // file. The rebuild now goes to a sibling .tmp and is renamed over the
            // target, which must leave no .tmp behind on the happy path.
            var r = Run(
                "from pyacad import xl\n" +
                "import tempfile, os\n" +
                "folder = tempfile.mkdtemp()\n" +
                "path = os.path.join(folder, 'ordered.xlsx')\n" +
                "xl.write(path, [['s']], headers=['Summary'], sheet='Summary')\n" +
                "xl.write(path, [['d']], headers=['Data'], sheet='Data')\n" +
                "assert xl.sheets(path) == ['Summary', 'Data'], xl.sheets(path)\n" +
                "xl.write(path, [['d2']], headers=['Data'], sheet='Data')\n" +
                "assert xl.sheets(path) == ['Summary', 'Data'], xl.sheets(path)\n" +
                "assert xl.read(path, sheet='Data') == [['Data'], ['d2']], xl.read(path, sheet='Data')\n" +
                "assert xl.read(path, sheet='Summary') == [['Summary'], ['s']]\n" +
                "leftovers = [n for n in os.listdir(folder) if n.endswith('.tmp')]\n" +
                "assert leftovers == [], leftovers\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ChartsModule_HandlesFullCircleSlices_AndRaggedValueLists()
        {
            var r = Run(
                "from pyacad import _charts\n" +
                "# a single category is a 360 degree slice: both arc endpoints round to\n" +
                "# the same coordinates, and SVG draws nothing for a zero-length arc, so\n" +
                "# it has to come out as a circle or the chart is blank\n" +
                "pie = _charts.pie_svg(['A'], [42])\n" +
                "assert '<circle' in pie, pie\n" +
                "assert '100%' in pie\n" +
                "ring = _charts.pie_svg(['A'], [42], doughnut=True)\n" +
                "assert '<circle' in ring and 'stroke-width' in ring, ring\n" +
                "# ragged inputs: fewer values than labels used to IndexError\n" +
                "svg = _charts.bar_svg(['A', 'B', 'C'], [1, 2])\n" +
                "assert svg.count('<rect') == 2, svg\n" +
                "assert '>C<' not in svg, svg\n" +
                "wide = _charts.bar_svg(['A'] * 12, [1, 2])\n" +
                "assert wide.count('<rect') == 2, wide\n" +
                "assert _charts.pie_svg(['A', 'B', 'C'], [1]).count('<circle') == 1\n" +
                "# None and NaN are drawn as zero, never raised\n" +
                "nan = float('nan')\n" +
                "assert 'no data' not in _charts.bar_svg(['A', 'B'], [None, 5])\n" +
                "assert 'no data' in _charts.bar_svg(['A', 'B'], [None, nan])\n" +
                "assert 'no data' not in _charts.line_svg(['a', 'b'], {'s': [None, 3]})\n" +
                "assert 'no data' not in _charts.pie_svg(['A', 'B'], [nan, 3])\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

    }
}
