using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public interface IGlobalLevel1Cache : ILevel1Cache
{
    // This space is intentionally left blank
}

public interface IGlobalLevel1Cache<T> : ILevel1Cache<T>
{
    // This space is intentionally left blank
}

public class GlobalLevel1Cache(IFusionCache cache)
    : Level1Cache("global", cache), IGlobalLevel1Cache
{
    // This space is intentionally left blank
}

public class GlobalLevel1Cache<T>(IFusionCache cache)
    : Level1Cache<T>("global", cache), IGlobalLevel1Cache<T>
{
    // This space is intentionally left blank
}
