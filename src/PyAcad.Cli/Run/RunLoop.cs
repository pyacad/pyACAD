using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using PyAcad.Shared;

namespace PyAcad.Cli.Run
{
    /// <summary>Where a run gets its host from: the year, the exe and the loader DLL.</summary>
    public sealed class RunHost
    {
        public string Year;
        public string ConsoleExe;
        public string LoaderDll;
    }

    /// <summary>
    /// <c>pyacad run</c>: one Core Console process per model, each handed a request file
    /// and leaving a response and a transcript in the run folder, summarised in
    /// results.json. Ctrl+C ends the current model and marks the rest not-run.
    /// </summary>
    public sealed class RunLoop
    {
        private readonly IProcessHost _processes;
        private readonly TextWriter _out;
        private readonly Func<DateTime> _clock;

        public RunLoop(IProcessHost processes, TextWriter output, Func<DateTime> clock = null)
        {
            _processes = processes;
            _out = output;
            _clock = clock ?? (() => DateTime.Now);
        }

        /// <summary>Exit code: 0 every model ok, 1 any model not ok, 3 cancelled.</summary>
        public int Run(RunArgs args, RunHost host, string runsRoot, CancellationToken cancel)
        {
            var script = Path.GetFullPath(args.Script);
            var models = new List<string>();
            foreach (var m in CoreConsole.MergeModels(args.Models, ReadModelsFile(args.ModelsFile)))
                models.Add(Path.GetFullPath(m));
            var allModels = models.ToArray();

            var started = _clock();
            var runDir = Path.Combine(runsRoot, started.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(runDir);
            var summary = new RunSummary { Script = script, Year = host.Year, StartedAt = started.ToString("o") };
            _out.WriteLine($"pyacad run: {Path.GetFileName(script)} on AutoCAD {host.Year}, run folder {runDir}");

            var slots = models.Count == 0 ? new List<string> { null } : models;
            var cancelled = false;
            for (var i = 0; i < slots.Count; i++)
            {
                var model = slots[i];
                var outcome = new ModelOutcome { Model = model };
                summary.Models.Add(outcome);
                if (cancelled)
                {
                    outcome.Status = "not-run";
                    continue;
                }

                var n = i + 1;
                var request = new RunRequest
                {
                    Script = script,
                    Model = model,
                    Models = allModels,
                    Args = args.ScriptArgs.ToArray(),
                    RunDir = runDir,
                    ResponsePath = Path.Combine(runDir, $"response-{n}.json"),
                    TranscriptPath = Path.Combine(runDir, $"transcript-{n}.txt"),
                    SaveModel = args.Save,
                };
                var requestPath = Path.Combine(runDir, $"request-{n}.json");
                request.Save(requestPath);
                var scriptPath = Path.Combine(runDir, $"run-{n}.scr");
                File.WriteAllText(scriptPath, CoreConsole.ScriptText(host.LoaderDll, requestPath), new UTF8Encoding(false));
                var consoleLog = Path.Combine(runDir, $"console-{n}.txt");

                _out.Write($"  [{n}/{slots.Count}] {(model == null ? "(no drawing)" : Path.GetFileName(model))} ... ");
                var end = _processes.Run(host.ConsoleExe, CoreConsole.Arguments(model, scriptPath),
                    args.TimeoutSeconds * 1000, consoleLog, cancel);

                RunResponse response = null;
                if (File.Exists(request.ResponsePath))
                {
                    try { response = RunResponse.Load(request.ResponsePath); }
                    catch (Exception ex) { File.AppendAllText(consoleLog, "Response unreadable: " + ex.Message + Environment.NewLine); }
                }

                if (end == ProcessEnd.Cancelled)
                {
                    outcome.Status = "cancelled";
                    cancelled = true;
                }
                else
                {
                    outcome.Status = CoreConsole.StatusOf(response?.Succeeded, response?.Saved ?? false,
                        end == ProcessEnd.TimedOut, args.Save, model != null);
                    if (end == ProcessEnd.FailedToStart) outcome.Status = "open-failed";
                }
                if (response != null)
                {
                    outcome.DurationMs = response.DurationMs;
                    outcome.ResultJson = response.ResultJson;
                    outcome.Error = CoreConsole.LastLine(response.Error) ?? response.SaveError;
                }
                else if (outcome.Status == "open-failed")
                {
                    outcome.Error = "No response from AutoCAD; see " + Path.GetFileName(consoleLog);
                }
                _out.WriteLine(outcome.Status + (outcome.Error != null ? ": " + outcome.Error : ""));
            }

            summary.FinishedAt = _clock().ToString("o");
            var resultsPath = Path.Combine(runDir, "results.json");
            summary.Save(resultsPath);
            if (!string.IsNullOrEmpty(args.ResultsPath))
            {
                try { File.Copy(resultsPath, args.ResultsPath, true); }
                catch (Exception ex) { _out.WriteLine("Could not copy results to " + args.ResultsPath + ": " + ex.Message); }
            }

            var failed = 0;
            foreach (var m in summary.Models) if (m.Status != "ok") failed++;
            _out.WriteLine(failed == 0
                ? $"Done: {summary.Models.Count} ok."
                : $"Done: {summary.Models.Count - failed} ok, {failed} not ok. Details in {resultsPath}");
            if (cancelled) return 3;
            return failed == 0 ? 0 : 1;
        }

        private static IEnumerable<string> ReadModelsFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return new string[0];
            return File.ReadAllLines(path);
        }
    }
}
