using System;
using System.Collections.Generic;
using System.Text;
namespace PyAcad.Cli.Run
{
    /// <summary>
    /// The pure half of driving accoreconsole.exe: the script it runs, the command line
    /// it gets, and what one model's outcome is called. Core Console starts, opens the
    /// drawing from /i, runs the /s script line by line, and exits; the script NETLOADs
    /// the pyACAD loader and hands it the request file through PYACADRUN.
    /// </summary>
    public static class CoreConsole
    {
        public const string ExeName = "accoreconsole.exe";

        /// <summary>The .scr text. Each line is one command; the closing newline is the
        /// Enter that ends the last one. A dot skips redefined commands, the underscore
        /// skips localized names.</summary>
        public static string ScriptText(string loaderDll, string requestPath)
        {
            var text = new StringBuilder();
            text.Append("_.NETLOAD \"").Append(loaderDll).Append("\"\n");
            text.Append("_.PYACADRUN \"").Append(requestPath).Append("\"\n");
            return text.ToString();
        }

        /// <summary>Arguments for accoreconsole: the drawing (none for a run without a
        /// model: Core Console opens a blank one), the script and a fixed locale so the
        /// underscored command names in the script resolve.</summary>
        public static string Arguments(string model, string scriptPath)
        {
            var args = new StringBuilder();
            if (!string.IsNullOrEmpty(model)) args.Append("/i \"").Append(model).Append("\" ");
            args.Append("/s \"").Append(scriptPath).Append("\" /l en-US");
            return args.ToString();
        }

        /// <summary>
        /// The status line of one model, from what the host left behind. No response at
        /// all means the loader never got to run the request: a drawing that would not
        /// open, a NETLOAD that failed, or a host that was killed on timeout.
        /// </summary>
        public static string StatusOf(bool? succeeded, bool saved, bool timedOut, bool saveAsked, bool hasModel)
        {
            if (timedOut) return "timed-out";
            if (succeeded == null) return "open-failed";
            if (succeeded == false) return "failed";
            if (!saveAsked) return "ok";
            if (!hasModel) return "save-skipped";
            return saved ? "ok" : "save-failed";
        }

        /// <summary>The last non-empty line of an error: where a traceback points.</summary>
        public static string LastLine(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return null;
            var lines = error.Replace("\r", "").Split('\n');
            for (var i = lines.Length - 1; i >= 0; i--)
                if (!string.IsNullOrWhiteSpace(lines[i])) return lines[i].Trim();
            return null;
        }

        /// <summary>Models from the command line plus the --models file, in that order,
        /// blank lines and # comments dropped, duplicates kept (a run twice is a choice).</summary>
        public static List<string> MergeModels(IEnumerable<string> positional, IEnumerable<string> fileLines)
        {
            var models = new List<string>(positional ?? new string[0]);
            foreach (var raw in fileLines ?? new string[0])
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                models.Add(line);
            }
            return models;
        }
    }
}
