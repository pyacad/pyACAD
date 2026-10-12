using System;
using PyAcad.Runtime.Bundles;

namespace PyAcad.Runtime.Execution
{
    /// <summary>
    /// The one way a ribbon click or a chord starts a bundle: send its registered command
    /// to the drawing, so the script runs in document context like a typed command. A
    /// bundle that got no command (name taken, no command host) runs directly under a
    /// document lock instead, so it still works, just without the command-line route.
    /// </summary>
    public static class CommandDispatch
    {
        /// <summary>Replaceable for tests: what a direct run does.</summary>
        internal static Action<PushButtonModel> DirectRun { get; set; } = ScriptExecutor.Run;
        internal static Action<PushButtonModel> DirectRunConfig { get; set; } = ScriptExecutor.RunConfig;

        /// <summary>The "open a drawing first" message, one place so the toast and tests agree.</summary>
        public const string NoDrawingMessage = "Open a drawing first.";

        public static void Run(PushButtonModel model) => Start(model, config: false);

        public static void RunConfig(PushButtonModel model) => Start(model, config: true);

        private static void Start(PushButtonModel model, bool config)
        {
            var name = CommandRegistry.NameFor(model, config);
            if (name != null)
            {
                if (CommandSender.Send(name)) return;
                // A send fails only without an active drawing, and a script cannot do
                // much in that state either; say so instead of running blind.
                Forms.Toast.Show("info", NoDrawingMessage, model.Title);
                return;
            }
            Host.DocumentLocks.Locked(() =>
            {
                if (config) DirectRunConfig(model);
                else DirectRun(model);
            });
        }
    }
}
