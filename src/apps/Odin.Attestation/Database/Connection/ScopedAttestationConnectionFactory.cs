using Autofac;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;

namespace Odin.Attestation.Database.Connection;

#nullable enable

public class ScopedAttestationConnectionFactory(
    ILifetimeScope lifetimeScope,
    ILogger<ScopedAttestationConnectionFactory> logger,
    IAttestationDbConnectionFactory connectionFactory,
    DatabaseCounters counters)
    : ScopedConnectionFactory<IAttestationDbConnectionFactory>(lifetimeScope, logger, connectionFactory, counters)
{
}
