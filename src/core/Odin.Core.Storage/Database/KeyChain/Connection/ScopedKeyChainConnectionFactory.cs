using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.KeyChain.Connection;

#nullable enable

public class ScopedKeyChainConnectionFactory(
    ILifetimeScope lifetimeScope,
    ILogger<ScopedKeyChainConnectionFactory> logger,
    IKeyChainDbConnectionFactory connectionFactory,
    CacheHelper cache,
    DatabaseCounters counters)
    : ScopedConnectionFactory<IKeyChainDbConnectionFactory>(lifetimeScope, logger, connectionFactory, cache, counters)
{
}
