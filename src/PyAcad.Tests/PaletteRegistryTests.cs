using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Panes;
using Xunit;

namespace PyAcad.Tests
{
    // Shares a collection with ToastTests: a broken pane raises a real toast on an STA thread.
    [Collection("Toasts")]
    public class PaletteRegistryTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "pyacad_tests", Guid.NewGuid().ToString("N"));

        public PaletteRegistryTests() { Directory.CreateDirectory(_root); }

        public void Dispose()
        {
            PaletteRegistry.Teardown();
            PaletteRegistry.CreatePalette = null;
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void Pane_ids_are_stable_across_case_slashes_and_whitespace_and_differ_per_key()
        {
            var a = PaneContent.IdFor("T.tab/P.panel/Nav.dockpane");
            Assert.Equal(a, PaneContent.IdFor(" t.tab\\p.panel\\nav.dockpane "));
            Assert.NotEqual(a, PaneContent.IdFor("T.tab/P.panel/Other.dockpane"));
            Assert.NotEqual(Guid.Empty, a);
        }

        [Fact]
        public void Load_returns_the_element_and_unwraps_a_window_root()
        {
            var grid = Path.Combine(_root, "grid.xaml");
            File.WriteAllText(grid, "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");
            var window = Path.Combine(_root, "window.xaml");
            File.WriteAllText(window,
                "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><StackPanel /></Window>");

            RunSta(() =>
            {
                Assert.IsType<System.Windows.Controls.Grid>(PaneContent.Load(grid));
                var unwrapped = PaneContent.Load(window);
                Assert.IsType<System.Windows.Controls.StackPanel>(unwrapped);
                Assert.Null(unwrapped.Parent);
            });
        }

        [Fact]
        public void Configure_builds_one_palette_per_dockpane_and_teardown_disposes_them()
        {
            var ext = ExtensionWithPane("Nav");
            var made = new List<FakePane>();
            PaletteRegistry.CreatePalette = (title, id, content, tip) =>
            {
                var pane = new FakePane { Title = title, Id = id };
                made.Add(pane);
                return pane;
            };

            RunSta(() =>
            {
                PaletteRegistry.Configure(new[] { ext });
                Assert.Single(made);
                Assert.Equal("Nav", made[0].Title);
                Assert.Same(made[0], PaletteRegistry.Find("T.tab/P.panel/Nav.dockpane"));
                PaletteRegistry.Teardown();
                Assert.True(made[0].Disposed);
                Assert.Null(PaletteRegistry.Find("T.tab/P.panel/Nav.dockpane"));
            });
        }

        [Fact]
        public void A_pane_whose_xaml_is_broken_costs_only_itself()
        {
            var ext = ExtensionWithPane("Good");
            var bad = ExtensionWithPane("Bad", xaml: "<NotXaml");
            var made = new List<string>();
            PaletteRegistry.CreatePalette = (title, id, content, tip) => { made.Add(title); return new FakePane(); };

            RunSta(() =>
            {
                try
                {
                    PaletteRegistry.Configure(new[] { bad, ext });
                    Assert.Equal(new[] { "Good" }, made);
                }
                finally
                {
                    // The problems toast is a window owned by this thread; closing it here
                    // keeps it out of Toast's live list, where a later CloseAll on another
                    // thread would trip over it.
                    PyAcad.Runtime.Forms.Toast.CloseAll();
                }
            });
        }

        [Fact]
        public void Pane_proxy_exposes_the_documented_members()
        {
            RunSta(() =>
            {
                var grid = (FrameworkElement)System.Windows.Markup.XamlReader.Parse(
                    "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><TextBlock x:Name=\"Summary\" /></Grid>");
                var handle = new FakePane();
                var proxy = new PaneProxy("Nav", "T.tab/P.panel/Nav.dockpane", grid, handle);

                Assert.Equal("Nav", proxy.Title);
                Assert.Equal("T.tab/P.panel/Nav.dockpane", proxy.BundleKey);
                Assert.Same(grid, proxy.Content);
                Assert.IsType<System.Windows.Controls.TextBlock>(proxy.Find("Summary"));
                Assert.Null(proxy.Find("Missing"));
                Assert.Null(proxy.Find(null));
                Assert.False(proxy.Visible);
                proxy.Visible = true;
                Assert.True(handle.Visible);
            });
        }

        private ExtensionModel ExtensionWithPane(string name, string xaml = null)
        {
            var dir = Path.Combine(_root, name + ".extension", "T.tab", "P.panel", name + ".dockpane");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pane.xaml"),
                xaml ?? "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");
            return BundleParser.ParseExtension(Path.Combine(_root, name + ".extension"));
        }

        private static void RunSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
        }

        private sealed class FakePane : IPaneHandle
        {
            public string Title;
            public Guid Id;
            public bool Disposed;
            public bool Visible { get; set; }
            public event Action VisibleChanged { add { } remove { } }
            public void Dispose() { Disposed = true; }
        }
    }
}
