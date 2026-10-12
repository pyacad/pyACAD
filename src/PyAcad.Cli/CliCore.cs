using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PyAcad.Shared;
using System.Xml.Linq;

namespace PyAcad.Cli
{
    /// <summary>
    /// The pyacad CLI's operations, decoupled from Main for testability: paths and
    /// AutoCAD-install resolution are injected, output goes to a TextWriter.
    /// </summary>
    public class CliCore
    {
        public const string SupportedVersionsNote = "2021-2026";

        /// <summary>The releases pyACAD builds for, oldest first.</summary>
        public static readonly string[] SupportedYears = { "2021", "2022", "2023", "2024", "2025", "2026" };

        /// <summary>The AutoCAD executable an install folder must hold to count as installed.</summary>
        public const string HostExe = "acad.exe";

        private readonly string _binRoot;
        private readonly string _configPath;
        private readonly string _bundleRoot;
        private readonly Func<string, string> _resolveAcadDir;
        private readonly TextWriter _out;

        /// <param name="bundleRoot">The per-user bundle folder, normally
        /// %APPDATA%\Autodesk\ApplicationPlugins\pyACAD.bundle.</param>
        public CliCore(string binRoot, string configPath, string bundleRoot, Func<string, string> resolveAcadDir, TextWriter output)
        {
            _binRoot = binRoot;
            _configPath = configPath;
            _bundleRoot = bundleRoot;
            _resolveAcadDir = resolveAcadDir;
            _out = output;
        }

        private string ContentsDir(string year) => Path.Combine(_bundleRoot, "Contents", year);
        private string ManifestPath => Path.Combine(_bundleRoot, "PackageContents.xml");

        public int Attach(string year)
        {
            if (!IsInstalled(year))
            {
                _out.WriteLine($"AutoCAD {year} not found (no {HostExe} in its install folder).");
                return 1;
            }

            var loader = Path.Combine(_binRoot, year, "PyAcad.dll");
            if (!File.Exists(loader))
            {
                _out.WriteLine($"No pyACAD build for {year}: {loader} missing (run: dotnet build -p:AcadVersion={year}).");
                return 1;
            }

            var dir = ContentsDir(year);
            Directory.CreateDirectory(dir);
            File.Copy(loader, Path.Combine(dir, "PyAcad.dll"), true);
            var pdb = Path.ChangeExtension(loader, ".pdb");
            if (File.Exists(pdb)) File.Copy(pdb, Path.Combine(dir, "PyAcad.pdb"), true);

            var doc = File.Exists(ManifestPath) ? XDocument.Load(ManifestPath) : NewManifest();
            RegisterLoader(doc, year);
            SaveManifest(doc);

            _out.WriteLine($"Attached: {Path.Combine(dir, "PyAcad.dll")} (registered in {ManifestPath})");
            return 0;
        }

        /// <summary>True when the bundle holds a loader for <paramref name="year"/>.</summary>
        public bool IsAttached(string year) => File.Exists(Path.Combine(ContentsDir(year), "PyAcad.dll"));

        /// <summary>True when AutoCAD <paramref name="year"/> is installed here
        /// with an acad.exe to start.</summary>
        public bool IsInstalled(string year)
        {
            var acadDir = _resolveAcadDir(year);
            return acadDir != null && File.Exists(Path.Combine(acadDir, HostExe));
        }

        /// <summary>AutoCAD's release number for a year ("R25.1" for 2026), or null.</summary>
        public static string SeriesOf(string year)
        {
            switch (year)
            {
                case "2021": return "R24.0";
                case "2022": return "R24.1";
                case "2023": return "R24.2";
                case "2024": return "R24.3";
                case "2025": return "R25.0";
                case "2026": return "R25.1";
                default: return null;
            }
        }

        /// <summary>The highest year the bundle holds a loader for AND AutoCAD is
        /// installed, or null when none. A loader attached for an API-only year must not
        /// become the year pyacad run starts by default.</summary>
        public string NewestAttachedYear()
        {
            var contents = Path.Combine(_bundleRoot, "Contents");
            if (!Directory.Exists(contents)) return null;
            return Directory.GetDirectories(contents)
                .Select(Path.GetFileName)
                .Where(name => name.Length == 4 && name.All(char.IsDigit) && IsAttached(name) && IsInstalled(name))
                .OrderByDescending(name => name, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        public int Detach(string year)
        {
            var dir = ContentsDir(year);
            var attached = Directory.Exists(dir);
            if (attached) Directory.Delete(dir, true);

            if (File.Exists(ManifestPath))
            {
                var doc = XDocument.Load(ManifestPath);
                var removed = UnregisterYear(doc, year);
                if (!doc.Root.Elements("Components").Any())
                {
                    File.Delete(ManifestPath);
                    var contents = Path.Combine(_bundleRoot, "Contents");
                    if (Directory.Exists(contents) && !Directory.EnumerateFileSystemEntries(contents).Any())
                        Directory.Delete(contents);
                    if (Directory.Exists(_bundleRoot) && !Directory.EnumerateFileSystemEntries(_bundleRoot).Any())
                        Directory.Delete(_bundleRoot);
                }
                else if (removed) SaveManifest(doc);
                attached |= removed;
            }

            if (!attached)
            {
                _out.WriteLine($"pyACAD is not attached to AutoCAD {year}.");
                return 0;
            }
            _out.WriteLine($"Detached: AutoCAD {year}.");
            return 0;
        }

        private static XDocument NewManifest()
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("ApplicationPackage",
                    new XAttribute("SchemaVersion", "1.0"),
                    new XAttribute("ProductType", "Application"),
                    new XAttribute("Name", "pyACAD"),
                    new XAttribute("Description", "Python scripting and ribbon tools for AutoCAD"),
                    new XAttribute("AppVersion", "0.0.0"),
                    new XAttribute("FriendlyVersion", "dev"),
                    new XAttribute("ProductCode", "{7F3E9A2C-5B14-4D6E-9C7A-2E8B1F4D6A90}"),
                    new XAttribute("UpgradeCode", "{A1C4E7B2-3D58-4F9A-8B6C-5E2D7A9F1C43}"),
                    new XAttribute("Author", "pyACAD"),
                    new XAttribute("SupportedLocales", "Enu"),
                    new XElement("CompanyDetails", new XAttribute("Name", "pyACAD"))));
        }

        private static XElement FindYear(XDocument doc, string year)
        {
            var marker = "/Contents/" + year + "/";
            return doc.Root.Elements("Components").FirstOrDefault(c =>
                c.Elements("ComponentEntry").Any(e =>
                    ((string)e.Attribute("ModuleName") ?? "").Replace('\\', '/')
                        .IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static void RegisterLoader(XDocument doc, string year)
        {
            var module = "./Contents/" + year + "/PyAcad.dll";
            var block = FindYear(doc, year);
            if (block == null)
            {
                var series = SeriesOf(year);
                // Platform "AutoCAD*" is the autoloader's wildcard for AutoCAD and every
                // vertical built on it (MEP, Architecture, Civil 3D share the series).
                block = new XElement("Components",
                    new XAttribute("Description", "AutoCAD " + year),
                    new XElement("RuntimeRequirements",
                        new XAttribute("OS", "Win64"),
                        new XAttribute("Platform", "AutoCAD*"),
                        new XAttribute("SeriesMin", series),
                        new XAttribute("SeriesMax", series)));
                doc.Root.Add(block);
            }
            if (block.Elements("ComponentEntry").Any(e =>
                    string.Equals((string)e.Attribute("ModuleName"), module, StringComparison.OrdinalIgnoreCase)))
                return;
            // LoadOnAutoCADStartup: the loader must run at startup to build its ribbon;
            // the default (on demand) would wait for a command nobody can type yet.
            block.AddFirst(new XElement("ComponentEntry",
                new XAttribute("AppName", "pyACAD"),
                new XAttribute("AppType", ".Net"),
                new XAttribute("LoadOnAutoCADStartup", "True"),
                new XAttribute("Version", (string)doc.Root.Attribute("AppVersion") ?? "0.0.0"),
                new XAttribute("ModuleName", module)));
        }

        private static bool UnregisterYear(XDocument doc, string year)
        {
            var block = FindYear(doc, year);
            if (block == null) return false;
            block.Remove();
            return true;
        }

        private void SaveManifest(XDocument doc)
        {
            Directory.CreateDirectory(_bundleRoot);
            var temp = ManifestPath + ".tmp";
            doc.Save(temp);
            if (File.Exists(ManifestPath)) File.Replace(temp, ManifestPath, null);
            else File.Move(temp, ManifestPath);
        }

        /// <summary>
        /// Where <c>pyacad run</c> gets its host: the year asked for or the newest attached
        /// one, accoreconsole.exe from that install, and the loader from the bundle (or the
        /// build folder when the year is not attached, for development). Null with a
        /// one-line reason when any of those is missing.
        /// </summary>
        public Run.RunHost ResolveRunHost(string year, out string error)
        {
            error = null;
            year = year ?? NewestAttachedYear();
            if (year == null)
            {
                error = "No attached AutoCAD. Run pyacad attach <year> first, or give --version.";
                return null;
            }
            if (Array.IndexOf(SupportedYears, year) < 0)
            {
                error = $"AutoCAD {year} is not supported ({SupportedVersionsNote}).";
                return null;
            }
            var acadDir = _resolveAcadDir(year);
            if (acadDir == null)
            {
                error = $"AutoCAD {year} is not installed.";
                return null;
            }
            var exe = Path.Combine(acadDir, Run.CoreConsole.ExeName);
            if (!File.Exists(exe))
            {
                error = $"AutoCAD {year} has no {Run.CoreConsole.ExeName} in {acadDir}.";
                return null;
            }
            var loader = Path.Combine(ContentsDir(year), "PyAcad.dll");
            if (!File.Exists(loader)) loader = Path.Combine(_binRoot, year, "PyAcad.dll");
            if (!File.Exists(loader))
            {
                error = $"No pyACAD loader for {year}: run pyacad attach {year}.";
                return null;
            }
            return new Run.RunHost { Year = year, ConsoleExe = exe, LoaderDll = loader };
        }

        public int Env()
        {
            _out.WriteLine("pyACAD environment");
            _out.WriteLine($"  config : {_configPath}{(File.Exists(_configPath) ? "" : "  (absent)")}");
            _out.WriteLine($"  builds : {_binRoot}");
            _out.WriteLine($"  bundle : {_bundleRoot}{(File.Exists(ManifestPath) ? "" : "  (absent)")}");
            foreach (var year in SupportedYears)
            {
                var acadDir = _resolveAcadDir(year);
                string state;
                if (acadDir == null)
                    state = "not installed";
                else if (!File.Exists(Path.Combine(acadDir, HostExe)))
                    state = $"not installed (no {HostExe})";
                else if (IsAttached(year))
                    state = "installed, pyACAD ATTACHED";
                else
                    state = "installed, not attached";

                var built = File.Exists(Path.Combine(_binRoot, year, "PyAcad.dll")) ? "built" : "no build";
                _out.WriteLine($"  AutoCAD {year} : {state} ({built})");
            }
            foreach (var path in ReadConfig().Extensions)
                _out.WriteLine($"  extension root : {path}");
            return 0;
        }

        public int ExtensionsList()
        {
            var extensions = ReadConfig().Extensions;
            if (extensions.Count == 0)
                _out.WriteLine("No extension roots configured.");
            foreach (var path in extensions)
                _out.WriteLine(path);
            return 0;
        }

        public int ExtensionsAdd(string path)
        {
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full))
            {
                _out.WriteLine($"Directory does not exist: {full}");
                return 1;
            }

            var config = ReadConfig();
            if (config.Extensions.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                _out.WriteLine($"Already configured: {full}");
                return 0;
            }

            config.Extensions.Add(full);
            config.Data["extensions"] = config.Extensions.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
            File.WriteAllText(_configPath, JsonShapes.Serialize(config.Data));
            _out.WriteLine($"Added extension root: {full}");
            return 0;
        }

        private sealed class ConfigState
        {
            public Dictionary<string, object> Data = new Dictionary<string, object>();
            public List<string> Extensions = new List<string>();
        }

        private ConfigState ReadConfig()
        {
            var state = new ConfigState();
            try
            {
                if (File.Exists(_configPath))
                {
                    state.Data = JsonShapes.ParseObject(File.ReadAllText(_configPath))
                        ?? new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                _out.WriteLine($"Warning: could not parse {_configPath} ({ex.Message}) - treating as empty.");
            }

            if (state.Data.TryGetValue("extensions", out var raw) && raw is IEnumerable list && !(raw is string))
                foreach (var item in list)
                    if (item is string s && !string.IsNullOrWhiteSpace(s))
                        state.Extensions.Add(s);
            return state;
        }
    }
}
