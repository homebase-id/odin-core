using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public interface ISystemLevel2Cache : ILevel2Cache
{
    // This space is intentionally left blank
}

public interface ISystemLevel2Cache<T> : ILevel2Cache<T>
{
    // This space is intentionally left blank
}

public class SystemLevel2Cache(IFusionCache cache)
    : Level2Cache("system", cache), ISystemLevel2Cache
{
    // This space is intentionally left blank
}

public class SystemLevel2Cache<T>(IFusionCache cache)
    : Level2Cache<T>("system", cache), ISystemLevel2Cache<T>
{
    // This space is intentionally left blank
}
