using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PyAcad.Cli;
using PyAcad.Cli.Run;
using PyAcad.Shared;
using Xunit;

namespace PyAcad.Tests
{
    public class RunLoopTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "pyacad_tests", Guid.NewGuid().ToString("N"));
        private readonly StringWriter _out = new StringWriter();
        private readonly string _script;
        private readonly RunHost _host;

        public RunLoopTests()
        {
            Directory.CreateDirectory(_dir);
            _script = Path.Combine(_dir, "count.py");
            File.WriteAllText(_script, "__result__ = 1\n");
            _host = new RunHost
            {
                Year = "2026",
                ConsoleExe = Path.Combine(_dir, "accoreconsole.exe"),
                LoaderDll = Path.Combine(_dir, "PyAcad.dll"),
            };
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static RunArgs Args(params string[] words)
        {
            var parsed = RunArgs.Parse(words, out var error);
            Assert.Null(error);
            return parsed;
        }

        [Fact]
        public void Script_text_and_arguments_are_what_core_console_expects()
        {
            Assert.Equal("_.NETLOAD \"C:\\x\\PyAcad.dll\"\n_.PYACADRUN \"C:\\r\\request-1.json\"\n",
                CoreConsole.ScriptText(@"C:\x\PyAcad.dll", @"C:\r\request-1.json"));
            Assert.Equal("/i \"C:\\m\\a.dwg\" /s \"C:\\r\\run-1.scr\" /l en-US",
                CoreConsole.Arguments(@"C:\m\a.dwg", @"C:\r\run-1.scr"));
            Assert.Equal("/s \"C:\\r\\run-1.scr\" /l en-US", CoreConsole.Arguments(null, @"C:\r\run-1.scr"));
        }

        [Theory]
        [InlineData(true, false, false, true, "ok")]
        [InlineData(false, false, false, true, "failed")]
        [InlineData(true, true, true, true, "ok")]
        [InlineData(true, true, false, true, "save-failed")]
        [InlineData(true, true, false, false, "save-skipped")]
        public void Status_follows_the_response_and_the_save_request(bool ok, bool saveAsked, bool saved, bool hasModel, string expected)
        {
            Assert.Equal(expected, CoreConsole.StatusOf(ok, saved, false, saveAsked, hasModel));
        }

        [Fact]
        public void No_response_is_open_failed_and_a_timeout_wins_over_everything()
        {
            Assert.Equal("open-failed", CoreConsole.StatusOf(null, false, false, false, true));
            Assert.Equal("timed-out", CoreConsole.StatusOf(true, false, true, false, true));
        }

        [Fact]
        public void Models_file_lines_join_the_positional_ones_minus_blanks_and_comments()
        {
            var merged = CoreConsole.MergeModels(new[] { "a.dwg" }, new[] { "# list", "", " b.dwg ", "c.dwg" });
            Assert.Equal(new[] { "a.dwg", "b.dwg", "c.dwg" }, merged);
        }

        [Fact]
        public void Each_model_gets_a_request_a_script_and_a_response_read_back_into_the_summary()
        {
            var a = Path.Combine(_dir, "a.dwg");
            var b = Path.Combine(_dir, "b.dwg");
            var host = new FakeProcessHost((args, request) =>
            {
                new RunResponse { Succeeded = request.Model == a, Error = request.Model == a ? null : "Trace\n  line\nValueError: bad", DurationMs = 5, ResultJson = "7" }
                    .Save(request.ResponsePath);
                return ProcessEnd.Completed;
            });
            var code = new RunLoop(host, _out, () => new DateTime(2026, 10, 5, 1, 2, 3)).Run(
                Args(_script, a, b, "--", "x"), _host, _dir, CancellationToken.None);

            Assert.Equal(1, code);
            Assert.Equal(2, host.Calls.Count);
            Assert.Contains("/i \"" + a + "\"", host.Calls[0].arguments);
            var runDir = Path.Combine(_dir, "20261005-010203");
            Assert.True(File.Exists(Path.Combine(runDir, "run-1.scr")));
            var request = RunRequest.Load(Path.Combine(runDir, "request-2.json"));
            Assert.Equal(b, request.Model);
            Assert.Equal(new[] { a, b }, request.Models);
            Assert.Equal(new[] { "x" }, request.Args);
            Assert.False(request.SaveModel);
            Assert.Contains("request-2.json\"", File.ReadAllText(Path.Combine(runDir, "run-2.scr")));

            var results = File.ReadAllText(Path.Combine(runDir, "results.json"));
            Assert.Contains("\"ok\"", results);
            Assert.Contains("\"failed\"", results);
            Assert.Contains("ValueError: bad", results);
            Assert.Contains("1 ok, 1 not ok", _out.ToString());
        }

        [Fact]
        public void No_model_runs_once_without_a_drawing_and_save_is_asked_through_the_request()
        {
            RunRequest seen = null;
            var host = new FakeProcessHost((args, request) =>
            {
                seen = request;
                new RunResponse { Succeeded = true }.Save(request.ResponsePath);
                return ProcessEnd.Completed;
            });
            var code = new RunLoop(host, _out).Run(Args(_script, "--save"), _host, _dir, CancellationToken.None);
            Assert.Equal(1, code);   // save-skipped: nothing to save
            Assert.Null(seen.Model);
            Assert.True(seen.SaveModel);
            Assert.DoesNotContain("/i ", host.Calls[0].arguments);
            Assert.Contains("save-skipped", _out.ToString());
        }

        [Fact]
        public void A_timeout_marks_the_model_and_a_cancel_marks_the_rest_not_run()
        {
            var a = Path.Combine(_dir, "a.dwg");
            var b = Path.Combine(_dir, "b.dwg");
            var c = Path.Combine(_dir, "c.dwg");
            var n = 0;
            var host = new FakeProcessHost((args, request) => ++n == 1 ? ProcessEnd.TimedOut : ProcessEnd.Cancelled);
            var code = new RunLoop(host, _out).Run(Args(_script, a, b, c, "--timeout", "5"), _host, _dir, CancellationToken.None);
            Assert.Equal(3, code);
            Assert.Equal(2, host.Calls.Count);
            Assert.Equal(5000, host.Calls[0].timeoutMs);
            var text = _out.ToString();
            Assert.Contains("timed-out", text);
            Assert.Contains("cancelled", text);
            var results = File.ReadAllText(Directory.GetFiles(_dir, "results.json", SearchOption.AllDirectories)[0]);
            Assert.Contains("\"not-run\"", results);
        }

        [Fact]
        public void Results_are_copied_where_asked()
        {
            var copy = Path.Combine(_dir, "out", "r.json");
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            var host = new FakeProcessHost((args, request) =>
            {
                new RunResponse { Succeeded = true }.Save(request.ResponsePath);
                return ProcessEnd.Completed;
            });
            var code = new RunLoop(host, _out).Run(Args(_script, "--results", copy), _host, _dir, CancellationToken.None);
            Assert.Equal(0, code);
            Assert.True(File.Exists(copy));
        }

        private sealed class FakeProcessHost : IProcessHost
        {
            private readonly Func<string, RunRequest, ProcessEnd> _behave;
            public readonly List<(string exe, string arguments, int timeoutMs)> Calls = new List<(string, string, int)>();

            public FakeProcessHost(Func<string, RunRequest, ProcessEnd> behave) { _behave = behave; }

            public ProcessEnd Run(string exe, string arguments, int timeoutMs, string consoleLogPath, CancellationToken cancel)
            {
                Calls.Add((exe, arguments, timeoutMs));
                // The .scr names the request; read it back the way the loader would.
                var scr = arguments.Substring(arguments.IndexOf("/s \"", StringComparison.Ordinal) + 4);
                scr = scr.Substring(0, scr.IndexOf('"'));
                var line = File.ReadAllText(scr).Split('\n')[1];
                var requestPath = line.Substring(line.IndexOf('"') + 1).TrimEnd('"');
                return _behave(arguments, RunRequest.Load(requestPath));
            }
        }
    }
}
