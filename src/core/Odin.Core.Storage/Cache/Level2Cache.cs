using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public interface ILevel2Cache : IFusionCacheWrapper
{
    // This space is intentionally left blank
}

//

public interface ILevel2Cache<T> : ILevel2Cache
{
    // This space is intentionally left blank
}

//

public abstract class Level2Cache : FusionCacheWrapper, ILevel2Cache
{
    private readonly FusionCacheEntryOptions _defaultOptions;

    //

    protected Level2Cache(string prefix, IFusionCache cache, CacheConfiguration config) : base(prefix + ":L2", cache)
    {
        _defaultOptions = cache.DefaultEntryOptions.Duplicate();
        _defaultOptions.SkipDistributedCacheRead = false;
        _defaultOptions.SkipDistributedCacheWrite = false;

        // Bypass level 1 cache if we need to explicitly test cache hits on level 2 cache:
        if (config.Level2CacheType != Level2CacheType.None && config.Level2BypassMemoryAccess)
        {
            _defaultOptions.SkipMemoryCacheRead = true;
            _defaultOptions.SkipMemoryCacheWrite = true;
        }
    }

    //

    protected override FusionCacheEntryOptions DefaultOptions => _defaultOptions;

    //
}

//

public abstract class Level2Cache<T>(string prefix, IFusionCache cache, CacheConfiguration config) :
    Level2Cache(prefix + ":" + typeof(T).FullName, cache, config),
    ILevel2Cache<T>
{
    // This space is intentionally left blank
}

