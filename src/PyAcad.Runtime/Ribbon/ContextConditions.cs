using System;
using System.Collections.Generic;

namespace PyAcad.Runtime.Ribbon
{
    /// <summary>
    /// Evaluates the context conditions against the live drawing, once per hub event
    /// (a snapshot), so N gated buttons cost one API sweep instead of N.
    /// </summary>
    public static class ContextConditions
    {
        /// <summary>One evaluator per name in <see cref="Bundles.ContextRule.KnownConditions"/>.</summary>
        internal static readonly IReadOnlyDictionary<string, Func<ContextProbe, bool>> Evaluators =
            new Dictionary<string, Func<ContextProbe, bool>>(StringComparer.OrdinalIgnoreCase)
            {
                ["doc"] = p => p.HasDocument,
                ["selection"] = p => p.HasSelection,
                ["modelspace"] = p => p.ModelSpace,
                ["paperspace"] = p => p.PaperSpace,
                ["xrefs"] = p => p.HasXrefs,
                ["blocks"] = p => p.HasBlocks,
                ["layouts"] = p => p.HasLayouts,
                ["multi-doc"] = p => p.MultiDocument,
            };

        /// <summary>Replaceable for tests; the default reads the active drawing.</summary>
        internal static Func<ContextProbe> ReadProbe { get; set; } = ReadFromHost;

        public static HashSet<string> Snapshot()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ContextProbe probe;
            try
            {
                probe = ReadProbe() ?? new ContextProbe();
            }
            catch (Exception ex)
            {
                // Outside AutoCAD (tests) or mid-command: every condition reads false.
                Log.Info("Context snapshot unavailable: " + ex.GetType().Name);
                return set;
            }
            foreach (var pair in Evaluators)
                if (pair.Value(probe)) set.Add(pair.Key);
            return set;
        }

        private static ContextProbe ReadFromHost() => ContextProbe.Read();
    }
}
