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
    /// The pure halves behind the Output panel (Batch Plot, Layout Manager): layout
    /// reordering and copy naming in pyacad.layouts, and the collision-safe PDF names
    /// in pyacad.plot, run through the real IronPython engine. The API halves added
    /// for the panel are pinned to fail with the helpful ImportError outside AutoCAD.
    /// </summary>
    public class PyAcadApiOutputTests
    {
        private readonly IronPythonEngine _engine;

        public PyAcadApiOutputTests()
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
        public void Layouts_Reorder_MovesOneName_AndLeavesTheInputAlone()
        {
            var r = Run(
                "from pyacad import layouts\n" +
                "names = ['A1', 'A2', 'A3', 'A4']\n" +
                "assert layouts.reorder(names, 'A3', 0) == ['A3', 'A1', 'A2', 'A4']\n" +
                "assert layouts.reorder(names, 'A1', 3) == ['A2', 'A3', 'A4', 'A1']\n" +
                "assert layouts.reorder(names, 'A1', 1) == ['A2', 'A1', 'A3', 'A4']\n" +
                "assert layouts.reorder(names, 'A4', 2) == ['A1', 'A2', 'A4', 'A3']\n" +
                "assert layouts.reorder(names, 'A2', 1) == names\n" +
                "assert names == ['A1', 'A2', 'A3', 'A4'], 'input mutated'\n" +
                "assert layouts.reorder(('A1', 'A2'), 'A2', 0) == ['A2', 'A1']\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Layouts_Reorder_ClampsTheIndex_MatchesCaseInsensitively_AndRejectsAStranger()
        {
            var r = Run(
                "from pyacad import layouts\n" +
                "names = ['A1', 'A2', 'A3']\n" +
                "assert layouts.reorder(names, 'A1', 99) == ['A2', 'A3', 'A1']\n" +
                "assert layouts.reorder(names, 'A3', -5) == ['A3', 'A1', 'A2']\n" +
                "assert layouts.reorder(names, 'A3', 1.0) == ['A1', 'A3', 'A2']\n" +
                "assert layouts.reorder(names, 'a3', 0) == ['A3', 'A1', 'A2']\n" +
                "try:\n" +
                "    layouts.reorder(names, 'B9', 0)\n" +
                "    raise AssertionError('no ValueError')\n" +
                "except ValueError as e:\n" +
                "    assert 'B9' in str(e), str(e)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Layouts_UniqueName_CountsUpFromTwo_PastWhatIsTaken()
        {
            var r = Run(
                "from pyacad import layouts\n" +
                "assert layouts.unique_name(['A1', 'A2'], 'B1') == 'B1'\n" +
                "assert layouts.unique_name(['A1', 'A2'], 'A1') == 'A1 (2)'\n" +
                "assert layouts.unique_name(['A1', 'A1 (2)'], 'A1') == 'A1 (3)'\n" +
                "assert layouts.unique_name(['A1', 'A1 (2)', 'A1 (3)'], 'A1') == 'A1 (4)'\n" +
                "assert layouts.unique_name(['A1', 'A1 (3)'], 'A1') == 'A1 (2)'\n" +
                "assert layouts.unique_name([], 'A1') == 'A1'\n" +
                "assert layouts.unique_name(iter(['A1']), 'A1') == 'A1 (2)'\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Layouts_UniqueName_IgnoresCase_ReusesAnExistingCounter_AndReservesModel()
        {
            var r = Run(
                "from pyacad import layouts\n" +
                // AutoCAD layout names are case-insensitive
                "assert layouts.unique_name(['a1'], 'A1') == 'A1 (2)'\n" +
                // copying 'A1 (2)' gives 'A1 (3)', not 'A1 (2) (2)'
                "assert layouts.unique_name(['A1', 'A1 (2)'], 'A1 (2)') == 'A1 (3)'\n" +
                "assert layouts.unique_name(['A1 (2)'], 'A1 (2)') == 'A1 (3)'\n" +
                "assert layouts.unique_name([], 'A1 (2)') == 'A1 (2)'\n" +
                // the model tab's name is always taken
                "assert layouts.unique_name([], 'Model') == 'Model (2)'\n" +
                "assert layouts.unique_name([], 'model') == 'model (2)'\n" +
                "assert layouts.unique_name(['A1'], '  A1  ') == 'A1 (2)'\n" +
                "try:\n" +
                "    layouts.unique_name(['A1'], '   ')\n" +
                "    raise AssertionError('no ValueError')\n" +
                "except ValueError:\n" +
                "    pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Plot_BatchNames_PairsEachLayoutWithAPdfInTheFolder_InOrder()
        {
            var r = Run(
                "from pyacad import plot\n" +
                "pairs = plot.batch_names(r'C:\\d\\Plan A.dwg', ['A1', 'A2'], r'C:\\out')\n" +
                "assert pairs == [('A1', r'C:\\out\\Plan A - A1.pdf'), ('A2', r'C:\\out\\Plan A - A2.pdf')], pairs\n" +
                "assert plot.DEFAULT_PATTERN == '{drawing} - {layout}.pdf'\n" +
                "pairs = plot.batch_names('x.dwg', ['B', 'A'], r'C:\\out', '{layout}.pdf')\n" +
                "assert pairs == [('B', r'C:\\out\\B.pdf'), ('A', r'C:\\out\\A.pdf')], pairs\n" +
                // a trailing separator on the folder is not doubled
                "pairs = plot.batch_names('x.dwg', ['A'], 'C:\\\\out\\\\', '{layout}.pdf')\n" +
                "assert pairs == [('A', r'C:\\out\\A.pdf')], pairs\n" +
                // no folder: bare file names
                "assert plot.batch_names('x.dwg', ['A'], '', '{layout}.pdf') == [('A', 'A.pdf')]\n" +
                "assert plot.batch_names('x.dwg', ['A'], None, '{layout}.pdf') == [('A', 'A.pdf')]\n" +
                "assert plot.batch_names('x.dwg', [], r'C:\\out') == []\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Plot_BatchNames_AddsThePdfExtension_AndCleansNamesThroughOutputName()
        {
            var r = Run(
                "from pyacad import plot\n" +
                "assert plot.batch_names('x.dwg', ['A'], r'C:\\o', '{layout}') == [('A', r'C:\\o\\A.pdf')]\n" +
                "assert plot.batch_names('x.dwg', ['A'], r'C:\\o', '{layout}.PDF') == [('A', r'C:\\o\\A.PDF')]\n" +
                "assert plot.batch_names('x.dwg', ['A/B:C'], r'C:\\o', '{layout}.pdf') == [('A/B:C', r'C:\\o\\A_B_C.pdf')]\n" +
                "assert plot.batch_names('untitled', ['L'], r'C:\\o') == [('L', r'C:\\o\\untitled - L.pdf')]\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Plot_BatchNames_NeverHandsTwoLayoutsTheSameFile()
        {
            var r = Run(
                "from pyacad import plot\n" +
                // 'A/1' and 'A:1' both clean to 'A_1'
                "pairs = plot.batch_names('x.dwg', ['A/1', 'A:1', 'A_1'], r'C:\\o', '{layout}.pdf')\n" +
                "assert pairs == [('A/1', r'C:\\o\\A_1.pdf'), ('A:1', r'C:\\o\\A_1 (2).pdf'), ('A_1', r'C:\\o\\A_1 (3).pdf')], pairs\n" +
                // a pattern without {layout} names every PDF alike
                "pairs = plot.batch_names(r'C:\\d\\Plan.dwg', ['A1', 'A2', 'A3'], r'C:\\o', '{drawing}.pdf')\n" +
                "assert [p for _, p in pairs] == [r'C:\\o\\Plan.pdf', r'C:\\o\\Plan (2).pdf', r'C:\\o\\Plan (3).pdf'], pairs\n" +
                // Windows file names ignore case
                "pairs = plot.batch_names('x.dwg', ['a', 'A'], r'C:\\o', '{layout}.pdf')\n" +
                "assert [p for _, p in pairs] == [r'C:\\o\\a.pdf', r'C:\\o\\A (2).pdf'], pairs\n" +
                // a counter never lands on a name a later layout plots to on its own
                "pairs = plot.batch_names('x.dwg', ['A', 'a', 'A (2)'], r'C:\\o', '{layout}.pdf')\n" +
                "paths = [p.lower() for _, p in pairs]\n" +
                "assert len(set(paths)) == 3, pairs\n" +
                "assert pairs[0] == ('A', r'C:\\o\\A.pdf'), pairs\n" +
                "assert pairs[2] == ('A (2)', r'C:\\o\\A (2).pdf'), pairs\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Theory]
        [InlineData("layouts.copy('A1', 'A2')")]
        [InlineData("layouts.set_order(['A2', 'A1'])")]
        [InlineData("layouts.apply_page_setup('Setup1', ['A1'])")]
        [InlineData("layouts.page_setups(paper_only=True)")]
        public void OutputApiFunctions_RaiseHelpfulImportError_OutsideAutoCAD(string call)
        {
            var r = Run(
                "from pyacad import layouts\n" +
                "try:\n" +
                "    " + call + "\n" +
                "    raise AssertionError('no ImportError')\n" +
                "except ImportError as e:\n" +
                "    assert 'AutoCAD' in str(e), str(e)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void OutputPanel_Ships_BatchPlot_And_LayoutManager_WithKeytipsAndADocContext()
        {
            var model = BundleParser.ParseExtension(ShippedExtensionDir());
            Assert.DoesNotContain(model.Problems,
                p => p.IndexOf("Output.panel", StringComparison.OrdinalIgnoreCase) >= 0);

            var output = model.Tabs.Single().Panels.Single(p => p.Title == "Output");
            var buttons = output.Items.OfType<PushButtonModel>().ToList();
            Assert.Equal(new[] { "Batch Plot", "Layout Manager" }, buttons.Select(b => b.Title).ToArray());
            Assert.Equal(new[] { "OP", "OL" }, buttons.Select(b => b.KeyTipOverride).ToArray());
            Assert.All(buttons, b =>
            {
                Assert.False(string.IsNullOrWhiteSpace(b.Tooltip));
                Assert.NotNull(b.ContextRule);
                Assert.True(File.Exists(b.ScriptPath), "missing " + b.ScriptPath);
            });
            Assert.Equal(output.Items.Count, buttons.Count);
            Assert.Empty(output.Slideout);
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

        [Fact]
        public void Layouts_Copy_RefusesTheModelTab_BeforeTouchingAutoCAD()
        {
            var r = Run(
                "from pyacad import layouts\n" +
                "for args in (('Model', 'A9'), ('A1', 'model')):\n" +
                "    try:\n" +
                "        layouts.copy(*args)\n" +
                "        raise AssertionError(args)\n" +
                "    except ValueError:\n" +
                "        pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }
    }
}
