using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity.Connection;

public class ScopedIdentityConnectionFactory(
    ILifetimeScope lifetimeScope,
    ILogger<ScopedIdentityConnectionFactory> logger,
    IIdentityDbConnectionFactory connectionFactory,
    CacheHelper cache)
    : ScopedConnectionFactory<IIdentityDbConnectionFactory>(lifetimeScope, logger, connectionFactory, cache)
{
}
