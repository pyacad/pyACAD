using System.Collections.Generic;
using PyAcad.Runtime.Bundles;
using PyAcad.Runtime.Events;
using PyAcad.Runtime.Ribbon;
using Xunit;

namespace PyAcad.Tests
{
    public class AcadEventMapTests
    {
        [Fact]
        public void A_named_drawing_arriving_is_an_open_and_an_unnamed_one_a_create()
        {
            Assert.Equal(AcadEvent.DocOpened, AcadEventMap.DocumentArrival(isNamedDrawing: true));
            Assert.Equal(AcadEvent.DocCreated, AcadEventMap.DocumentArrival(isNamedDrawing: false));
        }

        [Theory]
        [InlineData("CLAYER", AcadEvent.LayerChanged)]
        [InlineData("clayer", AcadEvent.LayerChanged)]
        [InlineData("OSMODE", AcadEvent.SystemVariableChanged)]
        [InlineData(null, AcadEvent.SystemVariableChanged)]
        public void Only_the_current_layer_variable_is_a_layer_change(string name, AcadEvent expected)
        {
            Assert.Equal(expected, AcadEventMap.SystemVariable(name));
        }

        [Fact]
        public void Object_events_are_wanted_only_when_a_hook_names_one()
        {
            var quiet = new ExtensionModel { Name = "a" };
            quiet.Hooks.Add(new HookModel { EventName = "doc-opened" });
            Assert.False(AcadEventMap.WantsObjectEvents(new[] { quiet }));

            var listener = new ExtensionModel { Name = "b" };
            listener.Hooks.Add(new HookModel { EventName = "object-erased" });
            Assert.True(AcadEventMap.WantsObjectEvents(new[] { quiet, listener }));
        }

        [Fact]
        public void Object_notifications_are_dropped_until_enabled()
        {
            var seen = new List<AcadEvent>();
            void Handler(AcadEventArgs a) => seen.Add(a.Event);
            AcadEvents.Raised += Handler;
            try
            {
                AcadEvents.ObjectEventsEnabled = false;
                AcadEvents.NotifyObject(AcadEvent.ObjectAppended);
                Assert.Empty(seen);
                AcadEvents.ObjectEventsEnabled = true;
                AcadEvents.NotifyObject(AcadEvent.ObjectAppended);   // not debounced: raises at once
                Assert.Equal(new[] { AcadEvent.ObjectAppended }, seen);
            }
            finally
            {
                AcadEvents.ObjectEventsEnabled = false;
                AcadEvents.Raised -= Handler;
            }
        }

        [Fact]
        public void Context_snapshot_reads_the_probe_and_names_every_true_condition()
        {
            var saved = ContextConditions.ReadProbe;
            try
            {
                ContextConditions.ReadProbe = () => new ContextProbe
                {
                    HasDocument = true, HasSelection = true, PaperSpace = true, HasLayouts = true,
                };
                var snapshot = ContextConditions.Snapshot();
                Assert.Equal(new[] { "doc", "layouts", "paperspace", "selection" },
                    new SortedSet<string>(snapshot));
            }
            finally { ContextConditions.ReadProbe = saved; }
        }

        [Fact]
        public void Context_snapshot_is_empty_when_the_host_cannot_be_read()
        {
            var saved = ContextConditions.ReadProbe;
            try
            {
                ContextConditions.ReadProbe = () => throw new System.IO.FileNotFoundException("acdbmgd");
                Assert.Empty(ContextConditions.Snapshot());
            }
            finally { ContextConditions.ReadProbe = saved; }
        }

        [Fact]
        public void Every_known_condition_has_an_evaluator_and_nothing_else_does()
        {
            Assert.Equal(new SortedSet<string>(ContextRule.KnownConditions),
                new SortedSet<string>(ContextConditions.Evaluators.Keys));
        }
    }
}
