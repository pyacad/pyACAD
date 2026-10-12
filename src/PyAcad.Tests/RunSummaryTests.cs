using System;
using System.Collections.Generic;
using System.IO;
using PyAcad.Shared;
using PyAcad.Cli;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>The results.json that <c>pyacad run</c> leaves in the run folder.</summary>
    public class RunSummaryTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "pyacad_tests", Guid.NewGuid().ToString("N"));

        public RunSummaryTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Save_EmbedsTheResultAsAnObject_AndNullWhenAbsent()
        {
            var summary = new RunSummary
            {
                Script = @"C:\scripts\audit.py",
                Year = "2026",
                StartedAt = "2026-10-04T09:30:00+02:00",
                FinishedAt = "2026-10-04T09:31:00+02:00",
            };
            summary.Models.Add(new ModelOutcome { Model = @"C:\m\a.dwg", Status = "ok", DurationMs = 1200, ResultJson = "{\"a\":1}" });
            summary.Models.Add(new ModelOutcome { Model = @"C:\m\b.dwg", Status = "failed", DurationMs = 5, Error = "Boom" });
            var path = Path.Combine(_dir, "results.json");

            summary.Save(path);

            var json = JsonShapes.ParseObject(File.ReadAllText(path));
            Assert.Equal(@"C:\scripts\audit.py", json["script"]);
            Assert.Equal("2026", json["year"]);
            Assert.Equal("2026-10-04T09:30:00+02:00", json["startedAt"]);
            Assert.Equal("2026-10-04T09:31:00+02:00", json["finishedAt"]);
            var models = (System.Collections.ArrayList)json["models"];
            var first = (Dictionary<string, object>)models[0];
            Assert.Equal(@"C:\m\a.dwg", first["model"]);
            Assert.Equal("ok", first["status"]);
            Assert.Equal(1200, Convert.ToInt32(first["durationMs"]));
            Assert.Null(first["error"]);
            var result = Assert.IsType<Dictionary<string, object>>(first["result"]);
            Assert.Equal(1, result["a"]);
            var second = (Dictionary<string, object>)models[1];
            Assert.Equal("failed", second["status"]);
            Assert.Equal("Boom", second["error"]);
            Assert.True(second.ContainsKey("result"));
            Assert.Null(second["result"]);
        }

        [Fact]
        public void Save_WritesTheSkippedAndNotRunStatuses_WithTheirMessages()
        {
            var summary = new RunSummary { Script = "audit.py", Year = "2026" };
            summary.Models.Add(new ModelOutcome { Model = @"C:\m\a.dxf", Status = "save-skipped", Error = "Only .dwg files are saved back." });
            summary.Models.Add(new ModelOutcome { Model = @"C:\m\b.dwg", Status = "not-run", Error = "Not run: the run ended after an earlier model." });
            var path = Path.Combine(_dir, "results.json");

            summary.Save(path);

            var json = JsonShapes.ParseObject(File.ReadAllText(path));
            var models = (System.Collections.ArrayList)json["models"];
            Assert.Equal("save-skipped", ((Dictionary<string, object>)models[0])["status"]);
            Assert.Equal("Only .dwg files are saved back.", ((Dictionary<string, object>)models[0])["error"]);
            Assert.Equal("not-run", ((Dictionary<string, object>)models[1])["status"]);
            Assert.Equal("Not run: the run ended after an earlier model.", ((Dictionary<string, object>)models[1])["error"]);
        }
    }
}
