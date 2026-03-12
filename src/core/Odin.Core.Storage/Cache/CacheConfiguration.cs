namespace Odin.Core.Storage.Cache;

#nullable enable

public record CacheConfiguration
{
    public required long MemoryCacheSizeLimit { get; init; }
    public required double MemoryCacheCompactionPercentage { get; init; }
    public required Level2CacheType Level2CacheType { get; init; }
}
