using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public interface ILevel1Cache : IFusionCacheWrapper
{
    // This space is intentionally left blank
}

public interface ILevel1Cache<T> : ILevel1Cache
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

public class Level1Cache<T>(CacheKeyPrefix prefix, IFusionCache cache) :
    Level1Cache(new CacheKeyPrefix(prefix + ":L1:" + typeof(T).FullName), cache),
    ILevel1Cache<T>
{
    // This space is intentionally left blank
}
