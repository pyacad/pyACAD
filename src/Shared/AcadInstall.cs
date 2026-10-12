using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

// One source, compiled into the runtime (HostInfo.InstallDir) and the command line
// (pyacad env / attach), which does not reference the runtime.
#if PYACAD_CLI
namespace PyAcad.Cli
#else
namespace PyAcad.Runtime.Host
#endif
{
    /// <summary>
    /// Where AutoCAD is installed, read from the registry the way the installer and the
    /// command line need it: HKLM\SOFTWARE\Autodesk\AutoCAD\{series}\{product}:{locale}
    /// carries AcadLocation per product. Pure over an IRegistry so tests need no AutoCAD.
    /// </summary>
    public static class AcadInstall
    {
        public interface IRegistry
        {
            IEnumerable<string> SubKeys(string path);
            string Value(string path, string name);
        }

        public const string RootKey = @"SOFTWARE\Autodesk\AutoCAD";

        private static readonly (int year, string series)[] Table =
        {
            (2021, "R24.0"), (2022, "R24.1"), (2023, "R24.2"), (2024, "R24.3"),
            (2025, "R25.0"), (2026, "R25.1"),
        };

        public static IEnumerable<int> SupportedYears => Table.Select(t => t.year);

        /// <summary>AutoCAD's release number for a supported year, or null.</summary>
        public static string SeriesOf(int year) =>
            Table.FirstOrDefault(t => t.year == year).series;

        /// <summary>
        /// The install folder of the plain AutoCAD of that year when it exists, else of
        /// any vertical built on it (MEP, Architecture, Civil 3D share the series key),
        /// else null.
        /// </summary>
        public static string FindInstallDir(int year, IRegistry registry = null)
        {
            var series = SeriesOf(year);
            if (series == null) return null;
            registry = registry ?? LocalMachine.Instance;
            var seriesKey = RootKey + "\\" + series;

            var candidates = new List<(string location, string product)>();
            foreach (var sub in registry.SubKeys(seriesKey))
            {
                var key = seriesKey + "\\" + sub;
                var location = registry.Value(key, "AcadLocation");
                if (string.IsNullOrEmpty(location)) continue;
                candidates.Add((location, registry.Value(key, "ProductName") ?? ""));
            }
            if (candidates.Count == 0) return null;

            // "AutoCAD 2024" is plain AutoCAD; "AutoCAD MEP 2024" or "Civil 3D" are verticals.
            var plain = candidates.FirstOrDefault(c => IsPlainAutoCad(c.product));
            return plain.location ?? candidates[0].location;
        }

        /// <summary>Supported years that have an install, ascending.</summary>
        public static IReadOnlyList<int> InstalledYears(IRegistry registry = null)
        {
            registry = registry ?? LocalMachine.Instance;
            return Table.Where(t => FindInstallDir(t.year, registry) != null).Select(t => t.year).ToList();
        }

        private static bool IsPlainAutoCad(string productName)
        {
            if (string.IsNullOrEmpty(productName)) return false;
            var words = productName.Split(' ');
            // Exactly "AutoCAD <year>" (an LT, MEP or Architecture name has a word between).
            return words.Length == 2 && words[0].Equals("AutoCAD", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The real HKLM, 64-bit view.</summary>
        private sealed class LocalMachine : IRegistry
        {
            public static readonly LocalMachine Instance = new LocalMachine();

            public IEnumerable<string> SubKeys(string path)
            {
                using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hive.OpenSubKey(path))
                    return key?.GetSubKeyNames() ?? new string[0];
            }

            public string Value(string path, string name)
            {
                using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hive.OpenSubKey(path))
                    return key?.GetValue(name) as string;
            }
        }
    }
}
