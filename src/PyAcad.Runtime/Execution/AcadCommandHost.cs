using System;
using System.Runtime.CompilerServices;
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
    }
}
