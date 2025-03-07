using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public interface ITenantLevel1Cache : ILevel1Cache
{
    // This space is intentionally left blank
}

public interface ITenantLevel1Cache<T> : ILevel1Cache<T>
{
    // This space is intentionally left blank
}

public class TenantLevel1Cache(CacheKeyPrefix prefix, IFusionCache cache, CacheConfiguration config)
    : Level1Cache(prefix, cache, config), ITenantLevel1Cache
{
    // This space is intentionally left blank
}

public class TenantLevel1Cache<T>(CacheKeyPrefix prefix, IFusionCache cache, CacheConfiguration config)
    : Level1Cache<T>(prefix, cache, config), ITenantLevel1Cache<T>
{
    // This space is intentionally left blank
}



