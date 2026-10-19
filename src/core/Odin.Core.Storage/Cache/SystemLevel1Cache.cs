using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public interface ISystemLevel1Cache : ILevel1Cache
{
    // This space is intentionally left blank
}

public interface ISystemLevel1Cache<T> : ILevel1Cache<T>
{
    // This space is intentionally left blank
}

public class SystemLevel1Cache(IFusionCache cache, IRemovalCounterFactory removals)
    : Level1Cache("system", cache, removals), ISystemLevel1Cache
{
    // This space is intentionally left blank
}

public class SystemLevel1Cache<T>(IFusionCache cache, IRemovalCounterFactory removals)
    : Level1Cache<T>("system", cache, removals), ISystemLevel1Cache<T>
{
    // This space is intentionally left blank
}
