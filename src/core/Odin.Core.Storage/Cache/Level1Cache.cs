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

public abstract class Level1Cache : FusionCacheWrapper, ILevel1Cache
{
    private readonly FusionCacheEntryOptions _defaultOptions;

    //

    protected Level1Cache(string prefix, IFusionCache cache) : base(prefix + ":L1", cache)
    {
        _defaultOptions = cache.DefaultEntryOptions.Duplicate();
        _defaultOptions.SkipDistributedCacheRead = true;
        _defaultOptions.SkipDistributedCacheWrite = true;
    }

    //

    protected override FusionCacheEntryOptions DefaultOptions => _defaultOptions;

    //
}

public abstract class Level1Cache<T>(string prefix, IFusionCache cache) :
    Level1Cache(prefix + ":" + typeof(T).FullName, cache), ILevel1Cache<T>
{
    // This space is intentionally left blank
}
