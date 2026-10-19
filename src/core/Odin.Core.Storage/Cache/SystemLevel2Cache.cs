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

public class SystemLevel2Cache(IFusionCache cache, IRemovalCounterFactory removals)
    : Level2Cache("system", cache, removals), ISystemLevel2Cache
{
    // This space is intentionally left blank
}

public class SystemLevel2Cache<T>(IFusionCache cache, IRemovalCounterFactory removals)
    : Level2Cache<T>("system", cache, removals), ISystemLevel2Cache<T>
{
    // This space is intentionally left blank
}
