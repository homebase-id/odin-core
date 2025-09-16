using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity;

public interface IIdentityTransactionalCacheFactory : ITransactionalCacheFactory
{
}

public class IdentityTransactionalCacheFactory(
    ITenantLevel2Cache cache,
    ITransactionalCacheStats cacheStats,
    ScopedIdentityConnectionFactory scopedConnectionFactory)
    : AbstractTransactionalCacheFactory(cache, cacheStats, scopedConnectionFactory), IIdentityTransactionalCacheFactory
{
}