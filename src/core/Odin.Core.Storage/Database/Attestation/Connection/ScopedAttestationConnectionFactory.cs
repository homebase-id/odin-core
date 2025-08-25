using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Attestation.Connection;

#nullable enable

public class ScopedAttestationConnectionFactory(
    ILifetimeScope lifetimeScope,
    ILogger<ScopedAttestationConnectionFactory> logger,
    IAttestationDbConnectionFactory connectionFactory,
    DatabaseCounters counters)
    : ScopedConnectionFactory<IAttestationDbConnectionFactory>(lifetimeScope, logger, connectionFactory, counters)
{
}
