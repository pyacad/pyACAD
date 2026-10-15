using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Internal;
using Autodesk.AutoCAD.Runtime;

namespace PyAcad.Runtime.Execution
{
    /// <summary>
    /// AutoCAD's runtime command API (AcMgd's Internal.Utils). Modal plus UsePickSet: the
    /// command runs in document context with the lock held, and a pickfirst selection
    /// made before typing the command survives into the script.
    /// </summary>
    internal sealed class AcadCommandHost : ICommandHost
    {
        private const CommandFlags Flags = CommandFlags.Modal | CommandFlags.UsePickSet;

        /// <summary>Null where AcMgd is not loaded (Core Console, tests).</summary>
        public static ICommandHost TryCreate()
        {
            if (Host.HostInfo.IsCoreConsole) return null;
            try
            {
                Probe();
                return new AcadCommandHost();
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Probe() => Utils.IsCommandDefined("LINE");

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Add(string group, string name, Action callback) =>
            Utils.AddCommand(group, name, name, Flags, new CommandCallback(callback));

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Remove(string group, string name) => Utils.RemoveCommand(group, name);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool IsNameInUse(string name) => Utils.IsCommandNameInUse(name) != CommandTypeFlags.NoneCmd;

        /// <summary>The acad.pgp AutoCAD loaded: the first one on the support path
        /// (ACADPREFIX), which is where AutoCAD itself looks.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public ISet<string> ReadPgpNames()
        {
            var support = Convert.ToString(Application.GetSystemVariable("ACADPREFIX")) ?? "";
            foreach (var dir in support.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string path;
                try { path = Path.Combine(dir.Trim(), "acad.pgp"); }
                catch (ArgumentException) { continue; }
                if (!File.Exists(path)) continue;
                Log.Info("Aliases checked against " + path);
                return PgpFile.Names(File.ReadAllText(path));
            }
            Log.Info("No acad.pgp on the support path; aliases checked against commands only.");
            return new HashSet<string>();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool IsLispCommand(string name)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            return doc != null && doc.GetLispSymbol("C:" + name) != null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RunLisp(string name) =>
            Application.DocumentManager.MdiActiveDocument?.SendStringToExecute("(C:" + name + ")\n", true, false, true);
    }
}
