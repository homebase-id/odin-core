using Autofac;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;

namespace Odin.Notarius.Database.Connection;

#nullable enable

public class ScopedNotaryConnectionFactory(
    ILifetimeScope lifetimeScope,
    ILogger<ScopedNotaryConnectionFactory> logger,
    INotaryDbConnectionFactory connectionFactory,
    DatabaseCounters counters)
    : ScopedConnectionFactory<INotaryDbConnectionFactory>(lifetimeScope, logger, connectionFactory, counters)
{
}
