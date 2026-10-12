using System.IO;
using PyAcad.Runtime.Config;

namespace PyAcad.Runtime.Engine
{
    /// <summary>
    /// Resolves the engine's global module search paths: python stdlib next to the
    /// runtime dll, and pyacadlib from config ("pyacadlib" key - dev points it at the
    /// repo checkout) with the installed layout (runtime dir) as fallback.
    /// </summary>
    public static class EngineConfigBuilder
    {
        public static EngineConfig Build(string runtimeDir, PyAcadConfig userConfig,
            System.Collections.Generic.IList<string> libraryDirs = null)
        {
            var config = new EngineConfig();

            var stdlib = Path.Combine(runtimeDir, "lib");
            if (Directory.Exists(stdlib))
                config.IronPythonPaths.Add(stdlib);
            else
                Log.Error($"Python stdlib not found at '{stdlib}' - 'import os' etc. will fail on IronPython.");

            var pyacadlib = ResolvePyAcadLib(runtimeDir, userConfig);
            if (pyacadlib != null)
                config.SearchPaths.Add(pyacadlib);
            else
                Log.Error("pyacadlib not found (config 'pyacadlib' key or <runtime>\\pyacadlib) - 'import pyacad' will fail.");

            if (libraryDirs != null)
                foreach (var lib in libraryDirs)
                    config.SearchPaths.Add(lib);

            config.CPythonDll = userConfig.CPythonPath;

            return config;
        }

        /// <summary>Directory containing the pyacad package, or null when nowhere on disk.</summary>
        public static string ResolvePyAcadLib(string runtimeDir, PyAcadConfig userConfig)
        {
            var configured = userConfig.PyAcadLibPath;
            if (!string.IsNullOrEmpty(configured))
            {
                if (Directory.Exists(configured)) return configured;
                Log.Error($"Configured pyacadlib '{configured}' does not exist - trying installed location.");
            }

            var installed = Path.Combine(runtimeDir, "pyacadlib");
            return Directory.Exists(installed) ? installed : null;
        }
    }
}
