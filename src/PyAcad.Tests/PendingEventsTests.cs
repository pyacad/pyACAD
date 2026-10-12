using PyAcad.Runtime.Events;
using PyAcad.Runtime.Execution;
using Xunit;

namespace PyAcad.Tests
{
    public class PendingEventsTests
    {
        [Fact]
        public void Take_Returns_What_Was_Queued_And_Empties()
        {
            var pending = new PendingEvents();
            pending.Add(AcadEvent.AppInit);
            pending.Add(AcadEvent.DocOpened);

            Assert.Equal(new[] { AcadEvent.AppInit, AcadEvent.DocOpened }, pending.Take());
            Assert.Empty(pending.Take());
        }

        [Fact]
        public void The_Same_Event_Queued_Twice_Comes_Back_Once()
        {
            var pending = new PendingEvents();
            pending.Add(AcadEvent.AppInit);
            pending.Add(AcadEvent.AppInit);

            Assert.Equal(new[] { AcadEvent.AppInit }, pending.Take());
        }
    }

    /// <summary>
    /// Reload raises app-init from INSIDE the Reload command, so a plain Raise is
    /// dropped by HookRunner's while-a-command-runs skip and app-init hooks never
    /// fire on Reload. RaiseWhenIdle holds it until the gate frees.
    /// </summary>
    public class RaiseWhenIdleTests
    {
        [Fact]
        public void With_Nothing_Running_It_Raises_Immediately()
        {
            RunGate.Exit();                       // tests share the statics
            var seen = 0;
            void Handler(AcadEventArgs a) { if (a.Event == AcadEvent.AppInit) seen++; }

            AcadEvents.Raised += Handler;
            try
            {
                AcadEvents.RaiseWhenIdle(AcadEvent.AppInit);
                Assert.Equal(1, seen);
            }
            finally { AcadEvents.Raised -= Handler; }
        }

        [Fact]
        public void While_A_Command_Runs_It_Waits_For_The_Flush()
        {
            RunGate.Exit();
            var seen = 0;
            void Handler(AcadEventArgs a) { if (a.Event == AcadEvent.AppInit) seen++; }

            AcadEvents.Raised += Handler;
            try
            {
                Assert.True(RunGate.TryEnter("Reload"));
                AcadEvents.RaiseWhenIdle(AcadEvent.AppInit);
                Assert.Equal(0, seen);            // held, not dropped

                RunGate.Exit();
                AcadEvents.FlushPending();
                Assert.Equal(1, seen);

                AcadEvents.FlushPending();       // nothing left to replay
                Assert.Equal(1, seen);
            }
            finally
            {
                AcadEvents.Raised -= Handler;
                RunGate.Exit();
            }
        }
    }
}
