using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public interface ILevel1Cache : IFusionCacheWrapper
{
    // This space is intentionally left blank
}

public class Level1Cache : FusionCacheWrapper, ILevel1Cache
{
    private readonly FusionCacheEntryOptions _defaultOptions;

    //

    public Level1Cache(CacheKeyPrefix prefix, IFusionCache cache) : base(prefix, cache)
    {
        _defaultOptions = cache.DefaultEntryOptions.Duplicate();
        _defaultOptions.SkipDistributedCacheRead = true;
        _defaultOptions.SkipDistributedCacheWrite = true;
    }

    //

    protected override FusionCacheEntryOptions DefaultOptions => _defaultOptions;

    //
}