using Xunit;

// Tests mutate the PHONIX_DATA_FILE env var and boot the singleton store, so run them serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
