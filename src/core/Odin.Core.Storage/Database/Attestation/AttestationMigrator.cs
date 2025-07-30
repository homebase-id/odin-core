using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Attestation.Connection;

namespace Odin.Core.Storage.Database.Attestation;

public partial class AttestationMigrator(
    ILogger<AttestationMigrator> logger, ScopedAttestationConnectionFactory scopedConnectionFactory) :
    AbstractMigrator(logger, scopedConnectionFactory)
{
}