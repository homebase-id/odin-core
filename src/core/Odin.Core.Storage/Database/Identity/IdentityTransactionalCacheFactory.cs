using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity;

public interface IIdentityTransactionalCacheFactory : ITransactionalCacheFactory
{
}

public class IdentityTransactionalCacheFactory(
    ILogger<IdentityTransactionalCacheFactory> logger,
    ITenantLevel2Cache cache,
    ITransactionalCacheStats cacheStats,
    ScopedIdentityConnectionFactory scopedConnectionFactory)
    : AbstractTransactionalCacheFactory(logger, cache, cacheStats, scopedConnectionFactory), IIdentityTransactionalCacheFactory
{
}