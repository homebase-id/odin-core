using Odin.Attestation.Database.Connection;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;

namespace Odin.Attestation.Database;

public partial class AttestationMigrator(
    ILogger<AttestationMigrator> logger,
    ScopedAttestationConnectionFactory scopedConnectionFactory,
    INodeLock nodeLock) :
    AbstractMigrator(logger, scopedConnectionFactory, nodeLock)
{
}