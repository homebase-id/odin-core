using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity.Connection;

#nullable enable

public class ScopedIdentityConnectionFactory(
    ILifetimeScope lifetimeScope,
    ILogger<ScopedIdentityConnectionFactory> logger,
    IIdentityDbConnectionFactory connectionFactory,
    CacheHelper cache,
    DatabaseCounters counters)
    : ScopedConnectionFactory<IIdentityDbConnectionFactory>(lifetimeScope, logger, connectionFactory, cache, counters)
{
}
