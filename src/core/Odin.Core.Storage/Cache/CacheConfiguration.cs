namespace Odin.Core.Storage.Cache;

#nullable enable

public class CacheConfiguration
{
    public Level2CacheType Level2CacheType { get; init; }
    public string? Level2Configuration { get; init; } = "";

    // NOTE: Set this to true when you need to TEST level 2 operations without first hitting level 1.
    // E.g. to make sure deserialization doesn't blow up during level 2 cache hits.
    // ONLY do this in testing!
    public bool Level2BypassMemoryAccess { get; init; } = false;
}
