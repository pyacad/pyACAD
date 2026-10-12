using Xunit;

// The runtime is a set of static singletons (event hub, toasts, ribbon markers, command
// registry, palette registry) and the WPF tests need one STA at a time, so test classes
// must not run side by side: three different flakes came from exactly that.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
