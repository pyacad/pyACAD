using System;

namespace PyAcad.Runtime.Host
{
    /// <summary>
    /// Everything the runtime asks of the host application, in one place. Each read goes
    /// through <see cref="AcadHostProbe"/> inside a try/catch, so outside AutoCAD (the
    /// test host, a tool run from the command line) the runtime gets a safe default
    /// instead of a type-load failure.
    /// </summary>
    public static class HostInfo
    {
        /// <summary>The host's main window, to own dialogs and place toasts; IntPtr.Zero
        /// (no window, as in the test host or Core Console) means "no owner" and every
        /// caller copes.</summary>
        public static IntPtr MainWindowHandle
        {
            get
            {
                try
                {
                    var handle = AcadHostProbe.MainWindowHandle();
                    if (handle != IntPtr.Zero) return handle;
                }
                catch (Exception) { /* no AutoCAD in this process */ }
                using (var self = System.Diagnostics.Process.GetCurrentProcess())
                    return self.MainWindowHandle;
            }
        }

        /// <summary>
        /// True when no one is at the screen, so Boot leaves out the ribbon, hooks,
        /// notices and the update check: the Core Console (accoreconsole.exe) or an
        /// AutoCAD that has no main window.
        /// </summary>
        public static bool IsHeadless
        {
            get
            {
                if (IsCoreConsole) return true;
                try { return !AcadHostProbe.HasMainWindow(); }
                catch (Exception) { return false; }
            }
        }

        /// <summary>True inside accoreconsole.exe, where AcMgd and the ribbon do not exist.</summary>
        public static bool IsCoreConsole
        {
            get
            {
                try
                {
                    using (var self = System.Diagnostics.Process.GetCurrentProcess())
                        return IsCoreConsoleProcess(self.ProcessName);
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>The pure rule behind <see cref="IsCoreConsole"/>.</summary>
        public static bool IsCoreConsoleProcess(string processName) =>
            processName != null && processName.Equals("accoreconsole", StringComparison.OrdinalIgnoreCase);

        /// <summary>Host release year (2021-2026), 0 when it cannot be told.</summary>
        public static int HostYear => Execution.HostGate.YearFromApiVersion(HostVersionString) ?? 0;

        /// <summary>The host's version as "major.minor..." text; "unknown" outside AutoCAD.</summary>
        public static string HostVersionString
        {
            get
            {
                try { return AcadHostProbe.VersionString(); }
                catch (Exception) { return "unknown"; }
            }
        }

        /// <summary>True when the host has a document open and active.</summary>
        public static bool HasActiveDocument
        {
            get
            {
                try { return AcadHostProbe.HasActiveDocument(); }
                catch (Exception) { return false; }
            }
        }

        /// <summary>How many drawings are open; 0 outside AutoCAD.</summary>
        public static int DocumentCount
        {
            get
            {
                try { return AcadHostProbe.DocumentCount(); }
                catch (Exception) { return 0; }
            }
        }

        /// <summary>The active drawing's full path, "untitled" when never saved, null when
        /// no drawing is open or outside AutoCAD.</summary>
        public static string ActiveDocumentPath
        {
            get
            {
                try { return AcadHostProbe.ActiveDocumentPath(); }
                catch (Exception) { return null; }
            }
        }

        /// <summary>The install folder of the AutoCAD release of that year, or null.</summary>
        public static string InstallDir(int year) => AcadInstall.FindInstallDir(year);
    }
}
