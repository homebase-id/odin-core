using System.Data.Common;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database.Connection.System;

#nullable enable

public class SqliteSystemDbConnectionFactory(string connectionString) : ISystemDbConnectionFactory
{
    public async Task<DbConnection> CreateAsync() => await SqliteConcreteConnectionFactory.Create(connectionString);
}
