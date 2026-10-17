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
    private readonly FusionCacheEntryOptions _defaultEntryOptions;

    //

    // Its entries stay on this node, but its tag removes do not: FusionCache keeps tag expirations in the distributed
    // cache, so another node's RemoveByTag reaches these entries without passing this wrapper. So the count comes
    // from the same factory as L2's, shared by every node when they share Redis.
    protected Level1Cache(string prefix, IFusionCache cache, IRemovalCounterFactory removals)
        : base(prefix + ":L1", cache, removals)
    {
        _defaultEntryOptions = cache.DefaultEntryOptions.Duplicate();
        _defaultEntryOptions.SkipDistributedCacheRead = true;
        _defaultEntryOptions.SkipDistributedCacheWrite = true;
    }

    //

    protected override FusionCacheEntryOptions DefaultEntryOptions => _defaultEntryOptions;

    //
}

public abstract class Level1Cache<T>(string prefix, IFusionCache cache, IRemovalCounterFactory removals) :
    Level1Cache(prefix + ":" + typeof(T).FullName, cache, removals), ILevel1Cache<T>
{
    // This space is intentionally left blank
}
