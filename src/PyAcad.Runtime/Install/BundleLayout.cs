using System;
using System.IO;

namespace PyAcad.Runtime.Install
{
    /// <summary>
    /// The per-user Autodesk bundle pyACAD installs into:
    ///   %APPDATA%\Autodesk\ApplicationPlugins\pyACAD.bundle\PackageContents.xml
    ///   %APPDATA%\Autodesk\ApplicationPlugins\pyACAD.bundle\Contents\{year}\PyAcad.dll
    /// AutoCAD reads the manifest at startup and loads the ComponentEntry whose
    /// series matches the running release, so no administrator rights are involved.
    /// </summary>
    public sealed class BundleLayout
    {
        public const string BundleFolderName = "pyACAD.bundle";
        public const string ManifestFileName = "PackageContents.xml";

        /// <summary>The bundle root folder (ends in pyACAD.bundle).</summary>
        public string Root { get; }
        /// <summary>The AutoCAD release year this loader belongs to, e.g. "2026".</summary>
        public string Year { get; }
        /// <summary>Contents\{year}: the folder holding the loader and any generated plugin.</summary>
        public string ContentsDir => Path.Combine(Root, "Contents", Year);
        public string ManifestPath => Path.Combine(Root, ManifestFileName);

        private BundleLayout(string root, string year)
        {
            Root = root;
            Year = year;
        }

        /// <summary>The bundle folder for the current user.</summary>
        public static string DefaultRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "ApplicationPlugins", BundleFolderName);

        /// <summary>AutoCAD's release number for a supported year: 2026 is R25.1.</summary>
        public static string SeriesFor(string year)
        {
            switch (year)
            {
                case "2021": return "R24.0";
                case "2022": return "R24.1";
                case "2023": return "R24.2";
                case "2024": return "R24.3";
                case "2025": return "R25.0";
                case "2026": return "R25.1";
                default: throw new ArgumentException("Not a supported release year: " + year);
            }
        }

        /// <summary>
        /// Recognises a loader path of the shape ...\{anything}.bundle\Contents\{year}\PyAcad.dll
        /// and returns its layout, or null for a loader that lives anywhere else (the
        /// developer's build folder, a test folder). Pure: nothing is touched on disk.
        /// </summary>
        public static BundleLayout FromLoaderPath(string loaderPath)
        {
            if (string.IsNullOrEmpty(loaderPath)) return null;
            var yearDir = Path.GetDirectoryName(loaderPath);
            if (yearDir == null) return null;
            var contentsDir = Path.GetDirectoryName(yearDir);
            if (contentsDir == null) return null;
            var root = Path.GetDirectoryName(contentsDir);
            if (root == null) return null;

            var year = Path.GetFileName(yearDir);
            int y;
            if (!int.TryParse(year, out y) || year.Length != 4) return null;
            if (!string.Equals(Path.GetFileName(contentsDir), "Contents", StringComparison.OrdinalIgnoreCase)) return null;
            if (!Path.GetFileName(root).EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)) return null;
            return new BundleLayout(root, year);
        }
    }
}
