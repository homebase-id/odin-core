using Odin.Core.Storage.Factory.Pgsql;

namespace Odin.Core.Storage.Database.Attestation.Connection;

#nullable enable

public class PgsqlAttestationDbConnectionFactory(string connectionString)
    : AbstractPgsqlDbConnectionFactory(connectionString), IAttestationDbConnectionFactory
{
}