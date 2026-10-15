using System;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices.Core;

namespace PyAcad.Runtime.Execution
{
    /// <summary>
    /// Sends a command name to the active drawing's command line, which is how Autodesk's
    /// own ribbon buttons run commands: the host then enters document context, locks the
    /// drawing and opens the undo group before our callback runs. Replaceable for tests.
    /// </summary>
    public static class CommandSender
    {
        /// <summary>Returns false when the send could not happen (no drawing open).</summary>
        public delegate bool Sender(string commandName);

        internal static Sender Override { get; set; }

        public static bool Send(string commandName)
        {
            if (string.IsNullOrEmpty(commandName)) return false;
            if (Override != null) return Override(commandName);
            try
            {
                return SendToActiveDocument(commandName);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not send command {commandName}", ex);
                return false;
            }
        }

        /// <summary>The text AutoCAD receives: the name, then a newline to execute it.
        /// A leading underscore skips localized command tables; a dot skips any
        /// redefinition, so a user's UNDEFINE never traps a pyACAD command. With
        /// cancelFirst, two Escapes lead (a menu macro's ^C^C), so a click while another
        /// command or a nested prompt is waiting ends it instead of typing into it.</summary>
        public static string Wire(string commandName, bool cancelFirst = false) =>
            (cancelFirst ? "\x03\x03" : "") + "_." + commandName + "\n";

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool SendToActiveDocument(string commandName)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            // Cancel only while something is running (CMDACTIVE counts commands,
            // transparent commands, scripts and AutoLISP prompts): an Escape at the idle
            // prompt would also drop the pickfirst set the tool is about to read.
            var busy = Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")) != 0;
            // activate: yes; wrapUpInactiveDoc: no; echo: yes, so the command line shows
            // what ran, the same as a typed command would.
            doc.SendStringToExecute(Wire(commandName, busy), true, false, true);
            return true;
        }
    }
}
