using System.Data.Common;
using System.Threading.Tasks;

namespace Odin.Core.Storage.RepositoryPattern.Connection.System;

#nullable enable

public class NpgsqlSystemDbConnectionFactory(string connectionString) : ISystemDbConnectionFactory
{
    public async Task<DbConnection> CreateAsync() => await NpgsqlConcreteConnectionFactory.Create(connectionString);
}
