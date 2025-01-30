using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public interface ILevel2Cache : IFusionCacheWrapper
{
    // This space is intentionally left blank
}

public class Level2Cache : FusionCacheWrapper, ILevel2Cache
{
    private readonly FusionCacheEntryOptions _defaultOptions;

    //

    public Level2Cache(CacheKeyPrefix prefix, IFusionCache cache) : base(prefix, cache)
    {
        _defaultOptions = cache.DefaultEntryOptions.Duplicate();
        _defaultOptions.SkipDistributedCacheRead = false;
        _defaultOptions.SkipDistributedCacheWrite = false;
    }

    //

    protected override FusionCacheEntryOptions DefaultOptions => _defaultOptions;

    //
}