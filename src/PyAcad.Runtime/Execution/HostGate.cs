using System.Collections.Generic;
using PyAcad.Runtime.Bundles;

namespace PyAcad.Runtime.Execution
{
    /// <summary>Bundle min/max host version check. Blocked buttons still render;
    /// the click warns and aborts (per design decision).</summary>
    public static class HostGate
    {
        /// <summary>Null when allowed to run; otherwise the toast message.</summary>
        public static string BlockMessage(PushButtonModel model, int hostYear)
        {
            // 0 is RuntimeHost's "could not parse the API version". A host we cannot
            // identify runs the tool: refusing would block every gated bundle at once.
            if (hostYear <= 0) return null;

            if (model.MinHostYear.HasValue && hostYear < model.MinHostYear.Value)
                return $"Requires AutoCAD {model.MinHostYear.Value} or newer (this is {hostYear}).";
            if (model.MaxHostYear.HasValue && hostYear > model.MaxHostYear.Value)
                return $"Requires AutoCAD {model.MaxHostYear.Value} or older (this is {hostYear}).";
            return null;
        }

        /// <summary>AutoCAD's internal release number (R24.0 ... R25.1) to its year.</summary>
        private static readonly Dictionary<string, int> Years = new Dictionary<string, int>
        {
            ["24.0"] = 2021,
            ["24.1"] = 2022,
            ["24.2"] = 2023,
            ["24.3"] = 2024,
            ["25.0"] = 2025,
            ["25.1"] = 2026,
        };

        /// <summary>The release year for a "major.minor[...]" AutoCAD version, or null
        /// when the text is not one of the releases pyACAD supports.</summary>
        public static int? YearFromApiVersion(string apiVersion)
        {
            if (string.IsNullOrEmpty(apiVersion)) return null;
            var parts = apiVersion.Split('.');
            if (parts.Length < 2) return null;
            if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor)) return null;
            return Years.TryGetValue(major + "." + minor, out var year) ? year : (int?)null;
        }
    }
}
