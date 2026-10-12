using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using PyAcad.Runtime.Engine;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// The pure halves behind the Layers panel (Layer Manager palette, Layer Mapper,
    /// Layer Snapshot), run through the real IronPython engine: the layer-standard CSV
    /// reader, the rename/merge/create plan it turns into, the palette's filter box and
    /// the snapshot slot picker. Plus the palette's pane.xaml, loaded the way the
    /// runtime loads it, with every element name its script.py looks up.
    /// </summary>
    public class PyAcadApiLayersTests
    {
        private readonly IronPythonEngine _engine;

        public PyAcadApiLayersTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyAcadLibTests.PyAcadLibDir);
            _engine.Initialize(config);
        }

        /// <summary>Runs the lines as one python script.</summary>
        private ExecResult Run(params string[] lines)
        {
            var request = new ScriptRequest { Code = string.Join("\n", lines) + "\n" };
            return _engine.Execute(request);
        }

        // ---- parse_mapping -----------------------------------------------------------

        [Fact]
        public void ParseMapping_ReadsOldNewColorLinetype_SkippingHeaderAndBlankLines()
        {
            var r = Run(
                "from pyacad import layers",
                "rows = layers.parse_mapping('old,new,color,linetype\\nA-WALL-OLD,A-WALL,1,DASHED\\n\\nE-*,ELEC\\n')",
                "assert rows == [('A-WALL-OLD', 'A-WALL', 1, 'DASHED'), ('E-*', 'ELEC', None, None)], rows",
                "assert layers.parse_mapping('A,B') == [('A', 'B', None, None)]",
                "assert layers.parse_mapping('') == []",
                "assert layers.parse_mapping(None) == []",
                "assert layers.parse_mapping('Old,New\\n') == []");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ParseMapping_IsTolerantOfQuotesBomLineEndingsBlankCellsAndExtraColumns()
        {
            var r = Run(
                "from pyacad import layers",
                // BOM, CRLF, quoted names with spaces and a doubled quote, padding,
                // a row of empty cells (Excel), an empty color with a linetype, a note column
                "text = u'\\ufeff\"Old Walls\" , \"Wall \"\"A\"\"\"\\r\\n\\r\\n , , \\r\\nX,Y,,HIDDEN,a note\\r\\n'",
                "rows = layers.parse_mapping(text)",
                "assert rows == [('Old Walls', 'Wall \"A\"', None, None), ('X', 'Y', None, 'HIDDEN')], rows",
                // the header may say from/to or old layer/new layer, any case
                "assert layers.parse_mapping('From,To\\nA,B') == [('A', 'B', None, None)]",
                "assert layers.parse_mapping('Old Layer,New Layer,Colour,Linetype\\nA,B,3') == [('A', 'B', 3, None)]",
                // a European Excel save uses semicolons; a paste from a sheet uses tabs
                "assert layers.parse_mapping('old;new;color\\nA;B;3\\n') == [('A', 'B', 3, None)]",
                "assert layers.parse_mapping('A\\tB\\t5\\tCENTER') == [('A', 'B', 5, 'CENTER')]",
                // colors by name, and a whole-number float straight out of a sheet
                "assert layers.parse_mapping('A,B,Red\\nC,D,blue\\nE,F,7.0') == "
                    + "[('A', 'B', 1, None), ('C', 'D', 5, None), ('E', 'F', 7, None)]");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Theory]
        [InlineData("A,,3", "line 1")]
        [InlineData("old,new\\nA,B,300", "line 2")]
        [InlineData("old,new\\n\\nA,B,0", "line 3")]
        [InlineData(",B", "line 1")]
        [InlineData("A,B,greenish", "line 1")]
        public void ParseMapping_RejectsARowWithoutANameOrWithABadColor_NamingTheLine(string csv, string where)
        {
            var r = Run(
                "from pyacad import layers",
                "try:",
                "    layers.parse_mapping('" + csv + "')",
                "    raise AssertionError('no ValueError')",
                "except ValueError as e:",
                "    assert '" + where + "' in str(e).lower(), str(e)");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        // ---- plan_mapping ------------------------------------------------------------

        [Fact]
        public void PlanMapping_RenamesWhenTheTargetIsFree_AndMergesWhenItExists()
        {
            var r = Run(
                "from pyacad import layers",
                "existing = ['A-WALL-OLD', 'A-WALL', 'e-lite', 'E-POWR', 'Doors']",
                "mapping = [('A-WALL-OLD', 'A-WALL', None, None), ('E-*', 'ELEC', None, None),",
                "           ('doors', 'A-DOOR', None, None)]",
                "plan = layers.plan_mapping(existing, mapping)",
                "assert plan == [('merge', 'A-WALL-OLD', 'A-WALL'), ('rename', 'e-lite', 'ELEC'),",
                "                ('merge', 'E-POWR', 'ELEC'), ('rename', 'Doors', 'A-DOOR')], plan");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PlanMapping_MatchesCaseInsensitively_FirstRowWins_AndUsesTheExistingTargetSpelling()
        {
            var r = Run(
                "from pyacad import layers",
                // 'a-*' claims A-OLD before the literal row can; the target keeps the
                // drawing's own spelling; a name that is already right does nothing;
                // a case-only difference is a rename
                "existing = ['A-OLD', 'Walls', 'walls-2', 'doors']",
                "mapping = [('a-*', 'walls', None, None), ('A-OLD', 'NEVER', None, None),",
                "           ('Walls', 'Walls', None, None), ('WALLS-?', 'WALLS', None, None),",
                "           ('doors', 'Doors', None, None)]",
                "plan = layers.plan_mapping(existing, mapping)",
                "assert [a for a in plan if a[0] != 'create'] == [('merge', 'A-OLD', 'Walls'),",
                "        ('merge', 'walls-2', 'Walls'), ('rename', 'doors', 'Doors')], plan");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PlanMapping_FollowsEarlierSteps_InDrawingOrder()
        {
            var r = Run(
                "from pyacad import layers",
                // B leaves for C first, so A can take the name B
                "plan = layers.plan_mapping(['B', 'A'], [('A', 'B', None, None), ('B', 'C', None, None)])",
                "assert plan == [('rename', 'B', 'C'), ('rename', 'A', 'B')], plan",
                // the second E- layer merges into the name the first one was renamed to
                "plan = layers.plan_mapping(['E-1', 'E-2', 'E-3'], [('E-*', 'ELEC', None, None)])",
                "assert plan == [('rename', 'E-1', 'ELEC'), ('merge', 'E-2', 'ELEC'), ('merge', 'E-3', 'ELEC')], plan",
                // a merged-away source is gone, so a later row may reuse its name
                "plan = layers.plan_mapping(['X', 'Y', 'Z'], [('X', 'Y', None, None), ('Z', 'X', None, None)])",
                "assert plan == [('merge', 'X', 'Y'), ('rename', 'Z', 'X')], plan");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PlanMapping_SkipsLayerZeroDefpointsXrefLayersAndInvalidNames_WithAReason()
        {
            var r = Run(
                "from pyacad import layers",
                "existing = ['0', 'Defpoints', 'XREF|E-LITE', 'Bad']",
                "plan = layers.plan_mapping(existing, [('0', 'Z', None, None), ('defpoints', 'Z', None, None),",
                "        ('*|*', 'Z', None, None), ('Bad', 'a<b', None, None)])",
                "skips = [a for a in plan if a[0] == 'skip']",
                "assert [s[1] for s in skips] == ['0', 'Defpoints', 'XREF|E-LITE', 'Bad'], plan",
                "assert 'cannot' in skips[0][2] and 'cannot' in skips[1][2], skips",
                "assert 'xref' in skips[2][2], skips",
                "assert 'a<b' in skips[3][2] and 'valid' in skips[3][2], skips",
                // the invalid target is reported once, not again as a layer it cannot create
                "assert len([a for a in plan if 'a<b' in str(a)]) == 1, plan",
                // a catch-all touches only what it may
                "plan = layers.plan_mapping(existing, [('*', 'MISC', None, None)])",
                "assert [a for a in plan if a[0] != 'skip'] == [('rename', 'Bad', 'MISC')], plan",
                "assert len([a for a in plan if a[0] == 'skip']) == 3, plan");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PlanMapping_CreatesEachMissingTargetOnce_AfterTheRenamesAndMerges()
        {
            var r = Run(
                "from pyacad import layers",
                "mapping = [('M-*', 'MECH', None, None), ('P-*', 'MECH', 4, 'DASHED'),",
                "           ('E-*', 'ELEC', 2, None), ('S-*', 'STRUCT', None, 'CENTER'), ('A-*', 'arch', 1, None)]",
                "plan = layers.plan_mapping(['E-1', 'ARCH'], mapping)",
                // MECH takes the first color and linetype any of its rows gives; ELEC came
                // from a rename and ARCH already exists, so neither is created
                "assert plan == [('rename', 'E-1', 'ELEC'), ('create', 'MECH', 4, 'DASHED'),",
                "                ('create', 'STRUCT', None, 'CENTER')], plan",
                "assert layers.plan_mapping([], []) == []");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void DescribeAction_ReadsAsASentence()
        {
            var r = Run(
                "from pyacad import layers",
                "assert layers.describe_action(('rename', 'A', 'B')) == 'Rename A to B'",
                "assert layers.describe_action(('merge', 'A', 'B')) == 'Merge A into B'",
                "assert layers.describe_action(('create', 'B', None, None)) == 'Create B'",
                "assert layers.describe_action(('create', 'B', 3, None)) == 'Create B (color 3)'",
                "assert layers.describe_action(('create', 'B', 3, 'DASHED')) == 'Create B (color 3, DASHED)'",
                "assert layers.describe_action(('create', 'B', None, 'DASHED')) == 'Create B (DASHED)'",
                "assert layers.describe_action(('skip', 'A', 'belongs to an xref')) == 'Skip A: belongs to an xref'");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        // ---- filter_names ------------------------------------------------------------

        [Fact]
        public void FilterNames_IsACaseInsensitiveSubstring_OrAWildcardPattern_OrSeveral()
        {
            var r = Run(
                "from pyacad import layers",
                "names = ['A-WALL', 'A-DOOR', 'E-LITE', 'Defpoints', '0']",
                "assert layers.filter_names(names, '') == names",
                "assert layers.filter_names(names, None) == names",
                "assert layers.filter_names(names, '   ') == names",
                "assert layers.filter_names(names, 'wall') == ['A-WALL']",
                "assert layers.filter_names(names, ' a- ') == ['A-WALL', 'A-DOOR']",
                "assert layers.filter_names(names, 'a-*') == ['A-WALL', 'A-DOOR']",
                "assert layers.filter_names(names, '*o*') == ['A-DOOR', 'Defpoints']",
                "assert layers.filter_names(names, '?-*') == ['A-WALL', 'A-DOOR', 'E-LITE']",
                "assert layers.filter_names(names, 'wall, LITE') == ['A-WALL', 'E-LITE']",
                "assert layers.filter_names(names, 'nothing') == []",
                "assert layers.filter_names(names, '[x]') == []",
                "kept = layers.filter_names(names, '')",
                "kept.append('X')",
                "assert len(names) == 5, 'filter_names must return a new list'");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        // ---- snapshot slots ----------------------------------------------------------

        [Fact]
        public void SnapshotChoices_ListsSlotsNewestFirst_WithWhenAndHowMany()
        {
            var r = Run(
                "from pyacad import layers",
                "slots = {",
                "    'Before plot': {'saved': '2026-10-11 09:30', 'layers': {'0': {}, 'A': {}}},",
                "    'Design': {'saved': '2026-10-12 14:05', 'layers': {'0': {}}},",
                "    'odd': {'layers': {}},",
                "    'broken': 'not a slot',",
                "}",
                "choices = layers.snapshot_choices(slots)",
                "assert [v for _, v in choices] == ['Design', 'Before plot', 'broken', 'odd'], choices",
                "assert choices[0][0] == 'Design  (saved 2026-10-12 14:05, 1 layer)', choices[0]",
                "assert choices[1][0] == 'Before plot  (saved 2026-10-11 09:30, 2 layers)', choices[1]",
                "assert layers.snapshot_choices({}) == []",
                "assert layers.snapshot_choices(None) == []");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        // ---- the Layer Manager palette -----------------------------------------------

        /// <summary>Every name Layer_Manager.dockpane\script.py passes to __pane__.Find.
        /// Written out on purpose: renaming an element should fail here.</summary>
        public static readonly string[] ManagerPaneNames =
        {
            "Root", "Filter", "Refresh", "LayerList", "Isolate", "FreezeThaw", "OnOff",
            "LockUnlock", "SetCurrent", "Merge", "Delete", "Status",
        };

        [Fact]
        public void LayerManagerPane_Parses_AsAPlainElement_WithEveryNameItsScriptFinds()
        {
            var path = Path.Combine(ShippedTabDir(), "Layers.panel", "Layer_Manager.dockpane", "pane.xaml");
            Assert.True(File.Exists(path), "missing " + path);

            var script = File.ReadAllText(Path.Combine(Path.GetDirectoryName(path), "script.py"));
            foreach (var name in ManagerPaneNames)
                Assert.Contains("'" + name + "'", script);

            OnSta(() =>
            {
                object root;
                using (var stream = File.OpenRead(path))
                    root = XamlReader.Load(stream);
                Assert.False(root is Window, "pane.xaml must not have a Window root");
                var element = Assert.IsAssignableFrom<FrameworkElement>(root);
                foreach (var name in ManagerPaneNames)
                    Assert.True(element.FindName(name) != null, "pane.xaml has no element named '" + name + "'");
            });
        }

        /// <summary>
        /// script.py run for real against the loaded pane.xaml, with pyacad.doc, layers
        /// and toast swapped for fakes: the rows bind by index and show, the filter box
        /// filters, the buttons follow the selection, a click acts and re-reads, and
        /// closing the last drawing empties the list instead of keeping stale rows.
        /// </summary>
        [Fact]
        public void LayerManagerScript_FillsFiltersAndActs_AgainstAFakeDrawing()
        {
            var dir = Path.Combine(ShippedTabDir(), "Layers.panel", "Layer_Manager.dockpane");
            var prelude = string.Join("\n",
                "import pyacad",
                "from pyacad import layers as real_layers",
                "class FakeLock(object):",
                "    def __enter__(self): return self",
                "    def __exit__(self, *exc): return False",
                "class FakeDoc(object):",
                "    open = True",
                "    def active(self):",
                "        if not self.open: raise RuntimeError('No drawing is open.')",
                "        return self",
                "    def lock(self): return FakeLock()",
                "class FakeLayers(object):",
                "    filter_names = staticmethod(real_layers.filter_names)",
                "    def __init__(self):",
                "        self.calls = []",
                "        self.state = {",
                "            '0': {'is_off': False, 'is_frozen': False, 'is_locked': False, 'color': 7},",
                "            'A-WALL': {'is_off': False, 'is_frozen': False, 'is_locked': True, 'color': 1},",
                "            'A-DOOR': {'is_off': True, 'is_frozen': False, 'is_locked': False, 'color': 2}}",
                "    def snapshot(self): return dict((k, dict(v)) for k, v in self.state.items())",
                "    def current(self): return '0'",
                "    def _flag(self, names, flag, value):",
                "        self.calls.append((flag, value, sorted(names)))",
                "        changed = [n for n in names if self.state[n][flag] != value]",
                "        for n in changed: self.state[n][flag] = value",
                "        return len(changed)",
                "    def freeze(self, names): return self._flag(names, 'is_frozen', True)",
                "    def thaw(self, names): return self._flag(names, 'is_frozen', False)",
                "class FakeToast(object):",
                "    def __init__(self): self.said = []",
                "    def _say(self, message, detail=None): self.said.append(message)",
                "    success = info = warning = error = _say",
                "fake_doc, fake_layers, fake_toast = FakeDoc(), FakeLayers(), FakeToast()",
                "pyacad.doc, pyacad.layers, pyacad.toast = fake_doc, fake_layers, fake_toast",
                "");
            var checks = string.Join("\n",
                "",
                "import clr",
                "from System.Windows import Point, Rect, RoutedEventArgs, Size",
                "from System.Windows.Controls import TextBlock",
                "from System.Windows.Controls.Primitives import ButtonBase",
                "from System.Windows.Media import VisualTreeHelper",
                "def shown_texts():",
                "    content = __pane__.Content",
                "    content.Measure(Size(520, 420))",
                "    content.Arrange(Rect(Point(0, 0), Size(520, 420)))",
                "    content.UpdateLayout()",
                "    found = []",
                "    def walk(node):",
                "        for i in range(VisualTreeHelper.GetChildrenCount(node)):",
                "            child = VisualTreeHelper.GetChild(node, i)",
                "            if isinstance(child, TextBlock): found.append(child.Text)",
                "            walk(child)",
                "    walk(__pane__.Find('LayerList'))",
                "    return found",
                "def click(name):",
                "    __pane__.Find(name).RaiseEvent(RoutedEventArgs(ButtonBase.ClickEvent))",
                "lst, status = __pane__.Find('LayerList'), __pane__.Find('Status')",
                "assert [str(r[0]) for r in lst.Items] == ['0', 'A-DOOR', 'A-WALL'], list(lst.Items)",
                "assert str(status.Text).startswith('3 layers'), status.Text",
                "texts = shown_texts()",
                "assert 'A-WALL' in texts and 'A-DOOR' in texts and u'\\u2713' in texts, texts",
                "assert not __pane__.Find('Isolate').IsEnabled",
                "__pane__.Find('Filter').Text = 'a-'",
                "assert [str(r[0]) for r in lst.Items] == ['A-DOOR', 'A-WALL'], list(lst.Items)",
                "lst.SelectAll()",
                "assert __pane__.Find('FreezeThaw').IsEnabled and not __pane__.Find('SetCurrent').IsEnabled",
                "click('FreezeThaw')",
                "assert fake_layers.calls == [('is_frozen', True, ['A-DOOR', 'A-WALL'])], fake_layers.calls",
                "assert fake_toast.said == ['Froze 2 layers'], fake_toast.said",
                "assert lst.SelectedItems.Count == 2, 'the selection survives the re-read'",
                "assert [str(r[3]) for r in lst.Items] == ['Yes', 'Yes'], [list(r) for r in lst.Items]",
                "click('FreezeThaw')",
                "assert fake_layers.calls[-1] == ('is_frozen', False, ['A-DOOR', 'A-WALL']), fake_layers.calls",
                "fake_doc.open = False",
                "click('Refresh')",
                "assert lst.Items.Count == 0 and str(status.Text) == 'No drawing is open.', status.Text");

            OnSta(() =>
            {
                FrameworkElement content;
                using (var stream = File.OpenRead(Path.Combine(dir, "pane.xaml")))
                    content = (FrameworkElement)XamlReader.Load(stream);
                var request = new ScriptRequest
                {
                    Code = prelude + File.ReadAllText(Path.Combine(dir, "script.py")) + checks + "\n",
                };
                request.Globals["__pane__"] = new PyAcad.Runtime.Panes.PaneProxy(
                    "Layer Manager", "Layers.panel/Layer_Manager.dockpane", content, null);
                var r = _engine.Execute(request);
                Assert.True(r.Succeeded, r.ErrorText);
            });
        }

        /// <summary>
        /// Layer Mapper's script.py with forms, layers' API half and toast faked: a
        /// UTF-8 CSV with a BOM is read, missing targets are offered, the ticked steps
        /// run in plan order whatever order the picker returns them in, and the
        /// counts are toasted.
        /// </summary>
        [Fact]
        public void LayerMapperScript_AppliesTheTickedSteps_InPlanOrder()
        {
            var dir = Path.Combine(ShippedTabDir(), "Layers.panel", "Layer_Mapper.pushbutton");
            var csv = Path.Combine(Path.GetTempPath(), "pyacad-layer-map-" + Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(csv, "old,new,color,linetype\r\nA-WALL-OLD,A-WALL\r\nE-*,ELEC,2\r\nM-*,MECH,4,DASHED\r\n0,ZERO\r\n",
                new System.Text.UTF8Encoding(true));
            try
            {
                var prelude = string.Join("\n",
                    "import pyacad",
                    "from pyacad import layers as real",
                    "class FakeDoc(object):",
                    "    def active(self): return self",
                    "class FakeLayers(object):",
                    "    parse_mapping = staticmethod(real.parse_mapping)",
                    "    plan_mapping = staticmethod(real.plan_mapping)",
                    "    describe_action = staticmethod(real.describe_action)",
                    "    def __init__(self): self.calls = []",
                    "    def names(self): return ['0', 'A-WALL-OLD', 'A-WALL', 'E-1', 'E-2']",
                    "    def rename(self, old, new): self.calls.append(('rename', old, new))",
                    "    def merge(self, old, new):",
                    "        self.calls.append(('merge', old, new))",
                    "        return 5",
                    "    def create(self, name, color, linetype): self.calls.append(('create', name, color, linetype))",
                    "class FakeForms(object):",
                    "    def __init__(self): self.asked = []",
                    "    def open_file(self, filter, title): return CSV_PATH",
                    "    def confirm(self, message, title=None):",
                    "        self.asked.append(message)",
                    "        return True",
                    "    def select_from_list(self, items, title=None, multiselect=False, prompt=None, checked=None):",
                    "        self.items, self.prompt, self.checked = items, prompt, checked",
                    "        return [v for l, v in reversed(items) if 'ZERO' not in l]",
                    "class FakeToast(object):",
                    "    def __init__(self): self.said = []",
                    "    def _say(self, message, detail=None): self.said.append((message, detail))",
                    "    success = info = warning = error = _say",
                    "fake_layers, fake_forms, fake_toast = FakeLayers(), FakeForms(), FakeToast()",
                    "pyacad.doc, pyacad.layers, pyacad.forms, pyacad.toast = FakeDoc(), fake_layers, fake_forms, fake_toast",
                    "");
                var checks = string.Join("\n",
                    "",
                    "assert 'MECH (color 4, DASHED)' in fake_forms.asked[0] and 'ZERO' in fake_forms.asked[0], fake_forms.asked",
                    "labels = [l for l, _ in fake_forms.items]",
                    "assert labels == ['Merge A-WALL-OLD into A-WALL', 'Rename E-1 to ELEC', 'Merge E-2 into ELEC',",
                    "                  'Create MECH (color 4, DASHED)', 'Create ZERO'], labels",
                    "assert fake_forms.checked == [v for _, v in fake_forms.items], 'every step starts ticked'",
                    "assert '1 layer left alone' in fake_forms.prompt and 'Defpoints' in fake_forms.prompt, fake_forms.prompt",
                    "assert fake_layers.calls == [('merge', 'A-WALL-OLD', 'A-WALL'), ('rename', 'E-1', 'ELEC'),",
                    "                             ('merge', 'E-2', 'ELEC'), ('create', 'MECH', 4, 'DASHED')], fake_layers.calls",
                    "assert fake_toast.said == [('Layer standard applied',",
                    "                            '1 renamed, 2 merged (10 objects moved), 1 created')], fake_toast.said");

                var request = new ScriptRequest
                {
                    Code = prelude + File.ReadAllText(Path.Combine(dir, "script.py")) + checks + "\n",
                };
                request.Globals["__commandpath__"] = dir;
                request.Globals["CSV_PATH"] = csv;
                var r = _engine.Execute(request);
                Assert.True(r.Succeeded, r.ErrorText);
            }
            finally
            {
                File.Delete(csv);
            }
        }

        private static string ShippedTabDir()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "extensions", "pyACAD.extension", "pyACAD.tab");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("shipped pyACAD.tab not found above test dir");
        }

        private static void OnSta(Action body)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }
    }
}
