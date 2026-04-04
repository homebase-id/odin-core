using Odin.Core.Storage.Factory.Pgsql;

namespace Odin.Attestation.Database.Connection;

#nullable enable

public class PgsqlAttestationDbConnectionFactory(string connectionString)
    : AbstractPgsqlDbConnectionFactory(connectionString), IAttestationDbConnectionFactory
{
}