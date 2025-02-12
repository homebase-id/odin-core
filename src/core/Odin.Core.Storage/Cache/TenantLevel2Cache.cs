using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public interface ITenantLevel2Cache : ILevel2Cache
{
    // This space is intentionally left blank
}

public interface ITenantLevel2Cache<T> : ILevel2Cache<T>
{
    // This space is intentionally left blank
}

public class TenantLevel2Cache(CacheKeyPrefix prefix, IFusionCache cache)
    : Level2Cache(prefix, cache), ITenantLevel2Cache
{
    // This space is intentionally left blank
}

public class TenantLevel2Cache<T>(CacheKeyPrefix prefix, IFusionCache cache)
    : Level2Cache<T>(prefix, cache), ITenantLevel2Cache<T>
{
    // This space is intentionally left blank
}
