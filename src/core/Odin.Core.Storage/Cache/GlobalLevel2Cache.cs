using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public interface IGlobalLevel2Cache : ILevel2Cache
{
    // This space is intentionally left blank
}

public interface IGlobalLevel2Cache<T> : ILevel2Cache<T>
{
    // This space is intentionally left blank
}

public class GlobalLevel2Cache(IFusionCache cache, CacheConfiguration config)
    : Level2Cache("global", cache, config), IGlobalLevel2Cache
{
    // This space is intentionally left blank
}

public class GlobalLevel2Cache<T>(IFusionCache cache, CacheConfiguration config)
    : Level2Cache<T>("global", cache, config), IGlobalLevel2Cache<T>
{
    // This space is intentionally left blank
}
