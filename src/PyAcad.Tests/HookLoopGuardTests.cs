using PyAcad.Runtime.Events;
using Xunit;

namespace PyAcad.Tests
{
    /// <summary>
    /// A hook that causes its own event must not run itself again: nested synchronously
    /// (layer-changed renaming a layer) it would recurse until the stack overflows
    /// and takes AutoCAD down; through a debounce (object-modified editing an object) it
    /// would loop four times a second for ever.
    /// </summary>
    public class HookLoopGuardTests
    {
        [Fact]
        public void AnEventRaisedWhileAHookRuns_DoesNotStartAnother()
        {
            var guard = new HookLoopGuard();

            Assert.True(guard.TryEnter());
            Assert.False(guard.TryEnter());
            guard.Exit();
            Assert.True(guard.TryEnter());
        }

        [Fact]
        public void ADebounceArmedInsideAHook_FiresAsHookCaused()
        {
            var guard = new HookLoopGuard();
            guard.TryEnter();
            guard.Armed(AcadEvent.ObjectModified);
            guard.Exit();

            Assert.True(guard.FiredFromHook(AcadEvent.ObjectModified));
        }

        [Fact]
        public void ADebounceArmedByTheUser_FiresNormally()
        {
            var guard = new HookLoopGuard();
            guard.Armed(AcadEvent.ObjectModified);

            Assert.False(guard.FiredFromHook(AcadEvent.ObjectModified));
        }

        [Fact]
        public void TheUserMovingAgainAfterTheHook_WinsOverTheHooksArming()
        {
            var guard = new HookLoopGuard();
            guard.TryEnter();
            guard.Armed(AcadEvent.SelectionChanged);
            guard.Exit();
            guard.Armed(AcadEvent.SelectionChanged);

            Assert.False(guard.FiredFromHook(AcadEvent.SelectionChanged));
        }

        [Fact]
        public void HookCaused_IsReportedOncePerFiring_AndPerEvent()
        {
            var guard = new HookLoopGuard();
            guard.TryEnter();
            guard.Armed(AcadEvent.ObjectModified);
            guard.Exit();

            Assert.False(guard.FiredFromHook(AcadEvent.SelectionChanged));
            Assert.True(guard.FiredFromHook(AcadEvent.ObjectModified));
            Assert.False(guard.FiredFromHook(AcadEvent.ObjectModified));
        }
    }
}
