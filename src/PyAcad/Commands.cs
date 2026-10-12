using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace PyAcad
{
    /// <summary>
    /// The commands the loader defines statically, so they exist even when the runtime
    /// failed to build a ribbon. Every bundle's own command is registered by the runtime
    /// at boot; these are the doors in when that did not happen, plus the LISP bridge.
    /// Each body hops into the runtime through a NoInlining seam, like Loader does.
    /// </summary>
    public class Commands
    {
        /// <summary>PYACAD: run a .py file by path, or a bundle by command name, with
        /// optional arguments. Also the Core Console entry point, where the script
        /// path arrives from the .scr file.</summary>
        [CommandMethod("PYACAD", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void PyAcad()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var options = new PromptStringOptions("\nScript path or pyACAD command: ") { AllowSpaces = true };
            var result = doc.Editor.GetString(options);
            if (result.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(result.StringResult)) return;
            try
            {
                var parts = SplitArguments(result.StringResult);
                var outcome = Invoke(parts[0], parts.GetRange(1, parts.Count - 1));
                if (!string.IsNullOrEmpty(outcome))
                    doc.Editor.WriteMessage("\npyACAD: " + outcome + "\n");
            }
            catch (System.Exception ex)
            {
                LoaderLog.ReportFatal("PYACAD failed.", ex);
            }
        }

        /// <summary>PYACADRUN "request.json": one pyacad run request, from the .scr the
        /// command line feeds to accoreconsole. Prints the outcome for the console log.</summary>
        [CommandMethod("PYACADRUN", CommandFlags.Modal)]
        public void PyAcadRun()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var options = new PromptStringOptions("\nRequest file: ") { AllowSpaces = true };
            var result = doc.Editor.GetString(options);
            if (result.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(result.StringResult)) return;
            var requestPath = result.StringResult.Trim().Trim('"');
            try
            {
                var code = RunRequest(requestPath);
                doc.Editor.WriteMessage("\npyACAD run exit " + code + "\n");
            }
            catch (System.Exception ex)
            {
                LoaderLog.Error("PYACADRUN failed: " + ex);
                doc.Editor.WriteMessage("\npyACAD run failed: " + ex.Message + "\n");
            }
        }

        /// <summary>PYACADRELOAD: rescan the extension folders and rebuild the ribbon.</summary>
        [CommandMethod("PYACADRELOAD", CommandFlags.Modal)]
        public void PyAcadReload()
        {
            try { Reload(); }
            catch (System.Exception ex) { LoaderLog.ReportFatal("PYACADRELOAD failed.", ex); }
        }

        /// <summary>PYACADCONSOLE: the engine and environment diagnostic.</summary>
        [CommandMethod("PYACADCONSOLE", CommandFlags.Modal)]
        public void PyAcadConsole()
        {
            try { Console(); }
            catch (System.Exception ex) { LoaderLog.ReportFatal("PYACADCONSOLE failed.", ex); }
        }

        /// <summary>(pyacad "name-or-path" args...) from AutoLISP; returns the script's __result__.</summary>
        [LispFunction("pyacad")]
        public object PyAcadLisp(ResultBuffer arguments)
        {
            try { return Lisp(arguments); }
            catch (System.Exception ex)
            {
                LoaderLog.Error("(pyacad ...) failed: " + ex);
                return null;
            }
        }

        /// <summary>The first token is the target; a quoted token keeps its spaces.</summary>
        internal static List<string> SplitArguments(string line)
        {
            var parts = new List<string>();
            var current = new System.Text.StringBuilder();
            var quoted = false;
            foreach (var ch in line)
            {
                if (ch == '"') { quoted = !quoted; continue; }
                if (char.IsWhiteSpace(ch) && !quoted)
                {
                    if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                    continue;
                }
                current.Append(ch);
            }
            if (current.Length > 0) parts.Add(current.ToString());
            return parts;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string Invoke(string target, List<string> args)
        {
            var values = new List<object>(args.Count);
            foreach (var a in args) values.Add(a);
            var outcome = Runtime.RuntimeHost.Invoke(target, values);
            return outcome.Succeeded ? null : outcome.ErrorText;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunRequest(string requestPath) =>
            Runtime.Execution.Runner.RunExecutor.RunRequestFile(requestPath);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Reload() => Runtime.RuntimeHost.Reload();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Console() => Runtime.RuntimeHost.RunConsoleFallback();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object Lisp(ResultBuffer arguments) => Runtime.Execution.LispBridge.Call(arguments);
    }
}
