using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Notary.Connection;

#nullable enable

public class ScopedNotaryConnectionFactory(
    ILifetimeScope lifetimeScope,
    ILogger<ScopedNotaryConnectionFactory> logger,
    INotaryDbConnectionFactory connectionFactory,
    CacheHelper cache,
    DatabaseCounters counters)
    : ScopedConnectionFactory<INotaryDbConnectionFactory>(lifetimeScope, logger, connectionFactory, cache, counters)
{
}
