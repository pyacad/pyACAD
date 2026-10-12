using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;

// One source, compiled into two assemblies: the runtime and the command line, which
// keeps no runtime reference. The CLI's copy is internal so the test project, which
// references both, sees one public PyAcad.Shared.JsonShapes.
namespace PyAcad.Shared
{
    /// <summary>
    /// System.Text.Json in the plain shapes the rest of the code walks: a JSON object is a
    /// Dictionary&lt;string, object&gt;, an array an ArrayList, a whole number an int (a
    /// long when it does not fit), any other number a decimal (a double when it does not
    /// fit, its JSON text when even that overflows), and true/false/null/strings their
    /// .NET selves. Nothing outside this file ever sees a JsonElement. These are the
    /// shapes JavaScriptSerializer produced, so code that walks parsed JSON did not
    /// change with the serializer.
    /// </summary>
#if PYACAD_CLI
    internal
#else
    public
#endif
    static class JsonShapes
    {
        private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions
        {
            // config.json is edited by hand: a stray trailing comma or a comment must not
            // cost someone every setting in the file.
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            // Keep non-ASCII and quotes readable; the files are read by people, not browsers.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Parses any JSON value into its plain shape. Throws on malformed text.</summary>
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            using (var doc = JsonDocument.Parse(json, ReadOptions))
                return ToShape(doc.RootElement);
        }

        /// <summary>Parses a JSON object; the text <c>null</c> gives null. Throws on
        /// malformed text, and with InvalidOperationException when the text holds some
        /// other value.</summary>
        public static Dictionary<string, object> ParseObject(string json)
        {
            var value = Parse(json);
            if (value == null) return null;
            if (value is Dictionary<string, object> map) return map;
            throw new InvalidOperationException("The JSON text is not an object.");
        }

        /// <summary>Converts one element; see the class summary for the shapes.</summary>
        public static object ToShape(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var map = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                        map[property.Name] = ToShape(property.Value);
                    return map;
                case JsonValueKind.Array:
                    var list = new ArrayList();
                    foreach (var item in element.EnumerateArray()) list.Add(ToShape(item));
                    return list;
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var i)) return i;
                    if (element.TryGetInt64(out var l)) return l;
                    if (element.TryGetDecimal(out var m)) return m;
                    // Out of double range too (1e400): keep the text rather than throw on
                    // net48 or hand back Infinity on net8.
                    if (element.TryGetDouble(out var d) && !double.IsInfinity(d)) return d;
                    return element.GetRawText();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }

        /// <summary>One-line JSON for a plain-shaped value (dictionaries, lists, arrays,
        /// strings, numbers, bools, null).</summary>
        public static string Serialize(object value) =>
            JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object), WriteOptions);
    }
}
