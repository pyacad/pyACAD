using PyAcad.Runtime.Events;
using Xunit;

namespace PyAcad.Tests
{
    public class EventNamesTests
    {
        [Fact]
        public void Every_Event_RoundTrips_Through_Its_Name()
        {
            foreach (AcadEvent evt in System.Enum.GetValues(typeof(AcadEvent)))
            {
                var name = EventNames.NameOf(evt);
                Assert.Matches("^[a-z-]+$", name);
                Assert.True(EventNames.TryParse(name, out var back));
                Assert.Equal(evt, back);
            }
        }

        [Fact]
        public void TryParse_Is_CaseInsensitive_And_Rejects_Unknown()
        {
            Assert.True(EventNames.TryParse("Doc-Opened", out var evt));
            Assert.Equal(AcadEvent.DocOpened, evt);
            Assert.False(EventNames.TryParse("no-such-event", out _));
            Assert.False(EventNames.TryParse(null, out _));
        }

        [Fact]
        public void Args_Name_Matches_Event()
        {
            var args = new AcadEventArgs { Event = AcadEvent.SelectionChanged };
            Assert.Equal("selection-changed", args.Name);
        }

        [Fact]
        public void The_Event_List_Is_Exactly_The_AutoCAD_Set()
        {
            Assert.Equal(new[]
            {
                "doc-created", "doc-opened", "doc-activated", "doc-closing", "doc-saved",
                "command-will-start", "command-ended", "command-cancelled", "command-failed",
                "before-command", "after-command", "selection-changed",
                "object-appended", "object-modified", "object-erased",
                "layer-changed", "layout-switched", "app-init", "app-quit",
                "system-variable-changed",
            }, EventNames.All);
        }

        [Fact]
        public void Every_Event_Has_A_Name_Of_Its_Own()
        {
            // Two events sharing a wire name would make one of them unreachable as a hook
            // file, and TryParse would silently resolve it to the other.
            var names = new System.Collections.Generic.List<string>();
            foreach (AcadEvent evt in System.Enum.GetValues(typeof(AcadEvent)))
                names.Add(EventNames.NameOf(evt));

            Assert.Equal(names.Count, new System.Collections.Generic.HashSet<string>(names).Count);
        }

        [Fact]
        public void Debounced_Events_Are_Known_Events()
        {
            Assert.NotEmpty(AcadEvents.Debounced);
            Assert.Contains(AcadEvent.SelectionChanged, AcadEvents.Debounced);
            foreach (var evt in AcadEvents.Debounced)
                Assert.True(EventNames.TryParse(EventNames.NameOf(evt), out _));
        }
    }
}
