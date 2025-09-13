using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity;

public interface IIdentityTransactionalCacheFactory : ITransactionalCacheFactory
{
}

public class IdentityTransactionalCacheFactory(
    ITenantLevel2Cache cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory)
    : AbstractTransactionalCacheFactory(cache, scopedConnectionFactory), IIdentityTransactionalCacheFactory
{
}