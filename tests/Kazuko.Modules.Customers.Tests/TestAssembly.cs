using Xunit;

// FirebirdDatabase has a process-wide profile; database fixtures must not overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
