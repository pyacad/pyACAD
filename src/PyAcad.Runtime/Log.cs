using System;
using System.IO;

namespace PyAcad.Runtime
{
    /// <summary>
    /// Runtime file logger: %APPDATA%\pyACAD\logs\pyACAD.log.
    /// There is no debugger story inside AutoCAD - this log IS the debugger.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static string LogDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyACAD", "logs");

        private static string LogPath
        {
            get
            {
                if (_path == null)
                {
                    Directory.CreateDirectory(LogDir);
                    _path = Path.Combine(LogDir, "pyACAD.log");
                }
                return _path;
            }
        }

        public static void Info(string message) => Write("INFO ", message);
        public static void Error(string message) => Write("ERROR", message);
        public static void Error(string message, Exception ex) => Write("ERROR", message + Environment.NewLine + ex);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                    File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never take the host down.
            }
        }
    }
}
