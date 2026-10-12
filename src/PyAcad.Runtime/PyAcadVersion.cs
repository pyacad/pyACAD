using System;
using System.IO;
using System.Reflection;

namespace PyAcad.Runtime
{
    /// <summary>
    /// The one version number. Directory.Build.props reads it from
    /// pyacad/__init__.py at build and stamps it into every assembly, so the loader,
    /// the runtime, the command line, the installer and the Python library all agree.
    /// This is what the boot log, the Settings window and PyAcadHost.Version show,
    /// so someone debugging an install can read it in three places.
    /// </summary>
    public static class PyAcadVersion
    {
        /// <summary>"1.0.0": the product version, not the four-part assembly one.</summary>
        public static string Product { get; } = ReadProduct();

        public static string RuntimePath => typeof(PyAcadVersion).Assembly.Location;

        /// <summary>The host's version text; "unknown" outside AutoCAD.</summary>
        public static string HostApiVersion => Host.HostInfo.HostVersionString;

        /// <summary>Host release year (2021-2026), 0 when it cannot be told.</summary>
        public static int HostYear => Host.HostInfo.HostYear;

        /// <summary>One line for a log or a bug report: version, host, runtime folder.</summary>
        public static string Summary()
        {
            var year = HostYear;
            var host = year > 0 ? "AutoCAD " + year : "AutoCAD (version unknown)";
            return $"pyACAD {Product} for {host}, runtime {RuntimePath}";
        }

        private static string ReadProduct()
        {
            var assembly = typeof(PyAcadVersion).Assembly;
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');            // strip a source-link build hash
                return plus > 0 ? info.Substring(0, plus) : info;
            }
            var v = assembly.GetName().Version;
            return v == null ? "unknown" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
