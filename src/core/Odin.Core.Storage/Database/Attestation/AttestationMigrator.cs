using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database.Attestation.Connection;

namespace Odin.Core.Storage.Database.Attestation;

public partial class AttestationMigrator(
    ILogger<AttestationMigrator> logger,
    ScopedAttestationConnectionFactory scopedConnectionFactory,
    INodeLock nodeLock) :
    AbstractMigrator(logger, scopedConnectionFactory, nodeLock)
{
}