using System;
using System.IO;
using System.Linq;

namespace PyAcad.Cli
{
    /// <summary>Thin arg-parser over CliCore with the real machine's paths.</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // exe lives in bin\cli\ -> builds live in bin\<year>\
            var binRoot = Path.GetDirectoryName(Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location));
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "pyACAD", "config.json");
            var bundleRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "ApplicationPlugins", "pyACAD.bundle");
            var cli = new CliCore(binRoot, configPath, bundleRoot, ResolveAcadDir, Console.Out);

            try
            {
                switch (args.Length > 0 ? args[0].ToLowerInvariant() : "")
                {
                    case "env":
                        return cli.Env();
                    case "attach" when args.Length == 2:
                        return cli.Attach(args[1]);
                    case "detach" when args.Length == 2:
                        return cli.Detach(args[1]);
                    case "extensions" when args.Length >= 2 && args[1] == "list":
                        return cli.ExtensionsList();
                    case "extensions" when args.Length >= 3 && args[1] == "add":
                        return cli.ExtensionsAdd(args[2]);
                    case "run":
                        return Run(cli, args);
                    case "protect-extension":
                    case "write-manifest":
                        return Upgrade.ExtensionGuard.Command(args, DateTime.Now, Console.Out);
                    default:
                        Console.WriteLine("pyacad - pyACAD command line");
                        Console.WriteLine();
                        Console.WriteLine("  pyacad env                     show detected AutoCAD versions and state");
                        Console.WriteLine($"  pyacad attach <year>           put the loader in the per-user bundle ({CliCore.SupportedVersionsNote})");
                        Console.WriteLine("  pyacad detach <year>           remove the loader from the bundle");
                        Console.WriteLine("  pyacad extensions list         show configured extension roots");
                        Console.WriteLine("  pyacad extensions add <dir>    add an extension root to config.json");
                        Console.WriteLine("  pyacad run <script.py> [drawings]  run a script in AutoCAD Core Console (pyacad run --help)");
                        Console.WriteLine("  pyacad protect-extension ...   (the installer's) back up and move your own work");
                        Console.WriteLine("                                  out of pyACAD.extension before an update");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return 1;
            }
        }

        private static int Run(CliCore cli, string[] args)
        {
            var runArgs = RunArgs.Parse(args.Skip(1).ToArray(), out var error);
            if (runArgs == null)
            {
                Console.WriteLine(error);
                Console.WriteLine(RunArgs.Usage);
                return 2;
            }
            if (runArgs.IsHelp)
            {
                Console.WriteLine(RunArgs.Usage);
                return 0;
            }
            if (!File.Exists(runArgs.Script))
            {
                Console.WriteLine("Script not found: " + runArgs.Script);
                return 2;
            }
            var host = cli.ResolveRunHost(runArgs.Year, out error);
            if (host == null)
            {
                Console.WriteLine(error);
                return 2;
            }

            var runsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyACAD", "runs");
            using (var cancel = new System.Threading.CancellationTokenSource())
            {
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; cancel.Cancel(); };
                return new Run.RunLoop(new Run.ProcessHost(), Console.Out).Run(runArgs, host, runsRoot, cancel.Token);
            }
        }

        private static string ResolveAcadDir(string year) =>
            int.TryParse(year, out var y) ? AcadInstall.FindInstallDir(y) : null;
    }
}
