using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System.Connection;

#nullable enable

public class ScopedSystemConnectionFactory(
    ILifetimeScope lifetimeScope,
    ILogger<ScopedSystemConnectionFactory> logger,
    ISystemDbConnectionFactory connectionFactory,
    DatabaseCounters counters)
    : ScopedConnectionFactory<ISystemDbConnectionFactory>(lifetimeScope, logger, connectionFactory, counters)
{
}
