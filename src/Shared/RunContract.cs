using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// One source, compiled into two assemblies: the runtime (the runner add-in reads the
// request and writes the response) and the command line (which writes the request and
// reads the response). The CLI keeps no runtime reference, so each gets its own copy.
// The CLI's copy is internal: the test project references both assemblies, and two
// public PyAcad.Shared.RunRequest types would be ambiguous there.
namespace PyAcad.Shared
{
    /// <summary>
    /// What <c>pyacad run</c> asks the runner add-in to do for one model: the file the
    /// CLI writes as request-N.json before it invokes the add-in. JSON keys are the
    /// lower-camel field names.
    /// </summary>
#if PYACAD_CLI
    internal
#else
    public
#endif
    sealed class RunRequest
    {
        /// <summary>Full path of the .py file.</summary>
        public string Script;
        /// <summary>Full path of the open model, or null for a run with no model.</summary>
        public string Model;
        /// <summary>Every model of the run, never null (empty ok).</summary>
        public string[] Models = new string[0];
        /// <summary>The arguments after "--", never null.</summary>
        public string[] Args = new string[0];
        /// <summary>The run folder.</summary>
        public string RunDir;
        /// <summary>Where the add-in writes the <see cref="RunResponse"/>.</summary>
        public string ResponsePath;
        /// <summary>Where the add-in writes the transcript text.</summary>
        public string TranscriptPath;
        /// <summary>Save the drawing back to its own path after a script that succeeded.</summary>
        public bool SaveModel;

        /// <summary>Reads a request; throws on a missing file or one that is not a JSON object.</summary>
        public static RunRequest Load(string path)
        {
            var json = RunJson.Read(path);
            return new RunRequest
            {
                Script = RunJson.String(json, "script"),
                Model = RunJson.String(json, "model"),
                Models = RunJson.Strings(json, "models"),
                Args = RunJson.Strings(json, "args"),
                RunDir = RunJson.String(json, "runDir"),
                ResponsePath = RunJson.String(json, "responsePath"),
                TranscriptPath = RunJson.String(json, "transcriptPath"),
                SaveModel = json.TryGetValue("save", out var save) && save is bool b && b,
            };
        }

        /// <summary>Writes the request as UTF-8 JSON, replacing any file at <paramref name="path"/>.</summary>
        public void Save(string path) => RunJson.Write(path, new Dictionary<string, object>
        {
            ["script"] = Script,
            ["model"] = Model,
            ["models"] = Models ?? new string[0],
            ["args"] = Args ?? new string[0],
            ["runDir"] = RunDir,
            ["responsePath"] = ResponsePath,
            ["transcriptPath"] = TranscriptPath,
            ["save"] = SaveModel,
        });
    }

    /// <summary>
    /// How one model's run went: the file the add-in writes as response-N.json and the
    /// CLI reads back. JSON keys are the lower-camel field names.
    /// </summary>
#if PYACAD_CLI
    internal
#else
    public
#endif
    sealed class RunResponse
    {
        /// <summary>True when the script ran to the end without raising.</summary>
        public bool Succeeded;
        /// <summary>Full traceback or message, null when the script succeeded.</summary>
        public string Error;
        /// <summary>How long the script took, in milliseconds.</summary>
        public long DurationMs;
        /// <summary>JSON text of the script's __result__, null when unset or not serialisable.</summary>
        public string ResultJson;
        /// <summary>Process id of the AutoCAD that ran the script (0 when unknown): the
        /// one process the CLI may end if it outlives the run.</summary>
        public int HostPid;
        /// <summary>True when the request asked for a save and the drawing was written.</summary>
        public bool Saved;
        /// <summary>Why the save did not happen, null when it did or was not asked for.</summary>
        public string SaveError;

        /// <summary>Reads a response; throws on a missing file or one that is not a JSON object.</summary>
        public static RunResponse Load(string path)
        {
            var json = RunJson.Read(path);
            return new RunResponse
            {
                Succeeded = json.TryGetValue("succeeded", out var succeeded) && succeeded is bool b && b,
                Error = RunJson.String(json, "error"),
                DurationMs = json.TryGetValue("durationMs", out var ms) && ms != null ? Convert.ToInt64(ms) : 0,
                ResultJson = RunJson.String(json, "resultJson"),
                HostPid = json.TryGetValue("hostPid", out var pid) && pid != null ? Convert.ToInt32(pid) : 0,
                Saved = json.TryGetValue("saved", out var saved) && saved is bool s && s,
                SaveError = RunJson.String(json, "saveError"),
            };
        }

        /// <summary>Writes the response as UTF-8 JSON, replacing any file at <paramref name="path"/>.</summary>
        public void Save(string path) => RunJson.Write(path, new Dictionary<string, object>
        {
            ["succeeded"] = Succeeded,
            ["error"] = Error,
            ["durationMs"] = DurationMs,
            ["resultJson"] = ResultJson,
            ["hostPid"] = HostPid,
            ["saved"] = Saved,
            ["saveError"] = SaveError,
        });
    }

    /// <summary>The JSON plumbing both contract files share.</summary>
    internal static class RunJson
    {
        public static Dictionary<string, object> Read(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var json = JsonShapes.Parse(text) as Dictionary<string, object>;
            if (json == null) throw new InvalidDataException(path + " does not hold a JSON object.");
            return json;
        }

        public static void Write(string path, Dictionary<string, object> json) =>
            File.WriteAllText(path, JsonShapes.Serialize(json), new UTF8Encoding(false));

        public static string String(Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out var value) ? value as string : null;

        public static string[] Strings(Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out var value) && value is IEnumerable items && !(value is string)
                ? items.Cast<object>().Select(item => item as string).ToArray()
                : new string[0];
    }
}
