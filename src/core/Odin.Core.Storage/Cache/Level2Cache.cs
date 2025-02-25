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

    protected Level2Cache(string prefix, IFusionCache cache) : base(prefix + ":L2", cache)
    {
        _defaultOptions = cache.DefaultEntryOptions.Duplicate();
        _defaultOptions.SkipDistributedCacheRead = false;
        _defaultOptions.SkipDistributedCacheWrite = false;
    }

    //

    protected override FusionCacheEntryOptions DefaultOptions => _defaultOptions;

    //
}

//

public abstract class Level2Cache<T>(string prefix, IFusionCache cache) :
    Level2Cache(prefix + ":" + typeof(T).FullName, cache),
    ILevel2Cache<T>
{
    // This space is intentionally left blank
}

