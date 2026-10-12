using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace PyAcad.Cli.Run
{
    /// <summary>How one host process ended.</summary>
    public enum ProcessEnd { Completed, TimedOut, Cancelled, FailedToStart }

    /// <summary>Starting and waiting on a process, faked in tests.</summary>
    public interface IProcessHost
    {
        /// <summary>Runs the exe to its end, a timeout (0 = none) or a cancel, with its
        /// console output appended to <paramref name="consoleLogPath"/>.</summary>
        ProcessEnd Run(string exe, string arguments, int timeoutMs, string consoleLogPath, CancellationToken cancel);
    }

    /// <summary>The real one: a hidden process with its output captured to a file.</summary>
    public sealed class ProcessHost : IProcessHost
    {
        public ProcessEnd Run(string exe, string arguments, int timeoutMs, string consoleLogPath, CancellationToken cancel)
        {
            var info = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // accoreconsole writes UTF-16 to stdout; read it as anything else and the
                // console log is unreadable (every other byte a NUL, CRLF as U+0A0D).
                StandardOutputEncoding = Encoding.Unicode,
                StandardErrorEncoding = Encoding.Unicode,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            };
            Process process;
            try
            {
                process = Process.Start(info);
                if (process == null) return ProcessEnd.FailedToStart;
            }
            catch (Exception ex)
            {
                File.AppendAllText(consoleLogPath, "Could not start " + exe + ": " + ex.Message + Environment.NewLine);
                return ProcessEnd.FailedToStart;
            }

            using (process)
            using (var log = new StreamWriter(consoleLogPath, true, new UTF8Encoding(false)) { AutoFlush = true })
            {
                var gate = new object();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (gate) log.WriteLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (gate) log.WriteLine("ERR " + e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var deadline = timeoutMs > 0 ? DateTime.UtcNow.AddMilliseconds(timeoutMs) : DateTime.MaxValue;
                while (!process.WaitForExit(250))
                {
                    if (cancel.IsCancellationRequested) return End(process, ProcessEnd.Cancelled);
                    if (DateTime.UtcNow >= deadline) return End(process, ProcessEnd.TimedOut);
                }
                process.WaitForExit();   // flush the async readers
                return ProcessEnd.Completed;
            }
        }

        private static ProcessEnd End(Process process, ProcessEnd why)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception) { /* already gone */ }
            return why;
        }
    }
}
