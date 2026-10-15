using System;
using System.Collections.Generic;
using System.Linq;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Execution;
using Xunit;

namespace PyAcad.Tests
{
    [Collection("CommandRegistry")]
    public class CommandRegistryTests : IDisposable
    {
        private readonly FakeCommandHost _host = new FakeCommandHost();

        public CommandRegistryTests()
        {
            CommandRegistry.Host = _host;
        }

        public void Dispose()
        {
            CommandRegistry.Unregister();
            CommandRegistry.Host = null;
            CommandSender.Override = null;
            CommandDispatch.DirectRun = ScriptExecutor.Run;
            CommandDispatch.DirectRunConfig = ScriptExecutor.RunConfig;
        }

        [Fact]
        public void Register_adds_every_assignment_under_the_pyacad_group()
        {
            var table = CommandNames.Assign(new[] { Button("Alpha", "a"), Button("Beta", "b", hasConfig: true) });
            var problems = CommandRegistry.Register(table, _ => { });
            Assert.Empty(problems);
            Assert.Equal(new[] { "PYALPHA", "PYBETA", "PYBETACFG" }, _host.Added.Select(a => a.name));
            Assert.All(_host.Added, a => Assert.Equal(CommandNames.Group, a.group));
            Assert.Equal(_host.Added.Select(a => a.name), CommandRegistry.RegisteredNames);
        }

        [Fact]
        public void Callback_runs_the_assignment_it_was_registered_for()
        {
            var a = Button("Alpha", "a");
            var b = Button("Beta", "b");
            var ran = new List<string>();
            CommandRegistry.Register(CommandNames.Assign(new[] { a, b }), x => ran.Add(x.Name + ":" + x.Model.Title));
            _host.Invoke("PYBETA");
            _host.Invoke("PYALPHA");
            Assert.Equal(new[] { "PYBETA:Beta", "PYALPHA:Alpha" }, ran);
        }

        [Fact]
        public void A_name_AutoCAD_already_owns_is_skipped_and_reported()
        {
            _host.InUse.Add("PYALPHA");
            var table = CommandNames.Assign(new[] { Button("Alpha", "a"), Button("Beta", "b") });
            var problems = CommandRegistry.Register(table, _ => { });
            Assert.Equal(new[] { "PYBETA" }, _host.Added.Select(a => a.name));
            Assert.Contains("PYALPHA", Assert.Single(problems));
            Assert.Null(CommandRegistry.NameFor(table.Assignments[0].Model));
        }

        [Fact]
        public void Reregistering_removes_the_old_set_first()
        {
            CommandRegistry.Register(CommandNames.Assign(new[] { Button("Alpha", "a") }), _ => { });
            CommandRegistry.Register(CommandNames.Assign(new[] { Button("Gamma", "g") }), _ => { });
            Assert.Equal(new[] { "PYALPHA" }, _host.Removed.Select(r => r.name));
            Assert.Equal(new[] { "PYGAMMA" }, CommandRegistry.RegisteredNames);
        }

        [Fact]
        public void A_host_that_throws_on_one_name_costs_only_that_name()
        {
            _host.ThrowOn.Add("PYBETA");
            var table = CommandNames.Assign(new[] { Button("Alpha", "a"), Button("Beta", "b"), Button("Gamma", "g") });
            var problems = CommandRegistry.Register(table, _ => { });
            Assert.Equal(new[] { "PYALPHA", "PYGAMMA" }, CommandRegistry.RegisteredNames);
            Assert.Contains("PYBETA", Assert.Single(problems));
        }

        [Fact]
        public void Dispatch_sends_the_registered_command_and_never_runs_directly()
        {
            var a = Button("Alpha", "a", hasConfig: true);
            CommandRegistry.Register(CommandNames.Assign(new[] { a }), _ => { });
            var sent = new List<string>();
            CommandSender.Override = name => { sent.Add(name); return true; };
            var direct = 0;
            CommandDispatch.DirectRun = _ => direct++;
            CommandDispatch.DirectRunConfig = _ => direct++;

            CommandDispatch.Run(a);
            CommandDispatch.RunConfig(a);

            Assert.Equal(new[] { "PYALPHA", "PYALPHACFG" }, sent);
            Assert.Equal(0, direct);
        }

        [Fact]
        public void Dispatch_runs_directly_when_the_bundle_has_no_command()
        {
            var a = Button("Alpha", "a");
            CommandRegistry.Unregister();
            var sent = 0;
            CommandSender.Override = _ => { sent++; return true; };
            PushButtonModel ran = null;
            CommandDispatch.DirectRun = m => ran = m;

            CommandDispatch.Run(a);

            Assert.Equal(0, sent);
            Assert.Same(a, ran);
        }

        [Fact]
        public void Wire_form_bypasses_localization_and_redefinition()
        {
            Assert.Equal("_.PYALPHA\n", CommandSender.Wire("PYALPHA"));
        }

        [Fact]
        public void Wire_form_cancels_a_running_command_first_when_asked()
        {
            Assert.Equal("\x03\x03_.PYALPHA\n", CommandSender.Wire("PYALPHA", cancelFirst: true));
        }

        [Fact]
        public void An_alias_registers_after_the_full_name_and_the_ribbon_keeps_the_full_name()
        {
            var a = Button("Alpha", "a", aliases: "AX");
            var problems = CommandRegistry.Register(CommandNames.Assign(new[] { a }), _ => { });

            Assert.Empty(problems);
            Assert.Equal(new[] { "PYALPHA", "AX" }, CommandRegistry.RegisteredNames);
            Assert.Equal("PYALPHA", CommandRegistry.NameFor(a));
            Assert.Equal(new[] { "AX" }, CommandRegistry.AliasesFor(a));
        }

        [Fact]
        public void A_bundle_alias_that_acad_pgp_or_AutoCAD_already_has_is_left_out_quietly()
        {
            _host.Pgp.Add("CO");
            _host.InUse.Add("LX");
            var a = Button("Alpha", "a", aliases: "CO LX AX");
            var problems = CommandRegistry.Register(CommandNames.Assign(new[] { a }), _ => { });

            Assert.Empty(problems);
            Assert.Equal(new[] { "PYALPHA", "AX" }, CommandRegistry.RegisteredNames);
            Assert.Equal(new[] { "AX" }, CommandRegistry.AliasesFor(a));
        }

        [Fact]
        public void A_users_own_alias_that_acad_pgp_has_is_left_out_and_reported()
        {
            _host.Pgp.Add("CO");
            var a = Button("Alpha", "a");
            var overrides = new Dictionary<string, string> { ["t/p/a.pushbutton"] = "CO" };
            var problems = CommandRegistry.Register(CommandNames.Assign(new[] { a }, overrides), _ => { });

            Assert.Contains("acad.pgp", Assert.Single(problems));
            Assert.Equal(new[] { "PYALPHA" }, CommandRegistry.RegisteredNames);
        }

        [Fact]
        public void An_alias_runs_its_tool_until_the_drawings_LISP_defines_the_same_name()
        {
            var a = Button("Alpha", "a", aliases: "AX");
            var ran = new List<string>();
            CommandRegistry.Register(CommandNames.Assign(new[] { a }), x => ran.Add(x.Name));

            _host.Invoke("AX");
            _host.Lisp.Add("AX");
            _host.Invoke("AX");
            _host.Invoke("PYALPHA");

            Assert.Equal(new[] { "AX", "PYALPHA" }, ran);
            Assert.Equal(new[] { "AX" }, _host.LispRuns);
        }

        [Fact]
        public void Acad_pgp_is_read_only_when_some_bundle_has_an_alias()
        {
            CommandRegistry.Register(CommandNames.Assign(new[] { Button("Alpha", "a") }), _ => { });
            Assert.Equal(0, _host.PgpReads);

            CommandRegistry.Register(CommandNames.Assign(new[] { Button("Beta", "b", aliases: "BX") }), _ => { });
            Assert.Equal(1, _host.PgpReads);
        }

        [Fact]
        public void A_pgp_that_cannot_be_read_still_registers_the_aliases()
        {
            _host.PgpThrows = true;
            var a = Button("Alpha", "a", aliases: "AX");
            CommandRegistry.Register(CommandNames.Assign(new[] { a }), _ => { });

            Assert.Equal(new[] { "AX" }, CommandRegistry.AliasesFor(a));
        }

        private static PushButtonModel Button(string title, string key, bool hasConfig = false, string aliases = null)
        {
            var model = new PushButtonModel
            {
                Title = title,
                BundleKey = "t/p/" + key + ".pushbutton",
                ScriptPath = @"C:\x\" + key + @"\script.py",
                ConfigScriptPath = hasConfig ? @"C:\x\" + key + @"\config.py" : null,
            };
            model.Aliases.AddRange(CommandNames.SplitAliases(aliases));
            return model;
        }

        private sealed class FakeCommandHost : ICommandHost
        {
            public readonly List<(string group, string name)> Added = new List<(string, string)>();
            public readonly List<(string group, string name)> Removed = new List<(string, string)>();
            public readonly HashSet<string> InUse = new HashSet<string>();
            public readonly HashSet<string> ThrowOn = new HashSet<string>();
            private readonly Dictionary<string, Action> _callbacks = new Dictionary<string, Action>();

            public void Add(string group, string name, Action callback)
            {
                if (ThrowOn.Contains(name)) throw new InvalidOperationException("refused " + name);
                Added.Add((group, name));
                _callbacks[name] = callback;
            }

            public void Remove(string group, string name)
            {
                Removed.Add((group, name));
                _callbacks.Remove(name);
            }

            public bool IsNameInUse(string name) => InUse.Contains(name);

            public readonly HashSet<string> Pgp = new HashSet<string>();
            public readonly HashSet<string> Lisp = new HashSet<string>();
            public readonly List<string> LispRuns = new List<string>();
            public int PgpReads;
            public bool PgpThrows;

            public ISet<string> ReadPgpNames()
            {
                PgpReads++;
                if (PgpThrows) throw new System.IO.IOException("locked");
                return new HashSet<string>(Pgp);
            }

            public bool IsLispCommand(string name) => Lisp.Contains(name);

            public void RunLisp(string name) => LispRuns.Add(name);

            public void Invoke(string name) => _callbacks[name]();
        }
    }
}
