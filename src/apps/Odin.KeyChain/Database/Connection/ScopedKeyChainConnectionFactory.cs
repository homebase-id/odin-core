using Autofac;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;

namespace Odin.KeyChain.Database.Connection;

#nullable enable

public class ScopedKeyChainConnectionFactory(
    ILifetimeScope lifetimeScope,
    ILogger<ScopedKeyChainConnectionFactory> logger,
    IKeyChainDbConnectionFactory connectionFactory,
    DatabaseCounters counters)
    : ScopedConnectionFactory<IKeyChainDbConnectionFactory>(lifetimeScope, logger, connectionFactory, counters)
{
}
