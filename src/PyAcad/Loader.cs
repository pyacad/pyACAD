using System;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(PyAcad.Loader))]

namespace PyAcad
{
    /// <summary>
    /// pyACAD bootstrap. AutoCAD calls Initialize once when it loads this assembly, which
    /// is what lets pyACAD build its ribbon and run extension startup scripts without
    /// waiting for a user command.
    /// </summary>
    public class Loader : IExtensionApplication
    {
        // Installed before any other code in this class runs, so the JIT never sees a
        // PyAcad.Runtime type before the resolver can find it.
        static Loader()
        {
            AssemblyResolver.Install();
        }

        public void Initialize()
        {
            LoaderLog.Info($"Loader.Initialize (loader for AutoCAD {LoaderInfo.AcadVersion})");
            try
            {
                Bootstrap();
            }
            catch (System.Exception ex)
            {
                LoaderLog.ReportFatal("pyACAD failed to start.", ex);
            }
        }

        public void Terminate()
        {
            LoaderLog.Info("Loader.Terminate");
        }

        // NoInlining keeps every PyAcad.Runtime type reference out of Initialize's JIT
        // compilation, so a missing runtime surfaces as a catchable exception here
        // instead of a type-load failure before the try block exists.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Bootstrap()
        {
            Runtime.RuntimeHost.Boot();
        }
    }
}
