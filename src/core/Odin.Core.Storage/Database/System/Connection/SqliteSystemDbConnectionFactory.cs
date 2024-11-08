using System.Data.Common;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.System.Connection;

#nullable enable

public class SqliteSystemDbConnectionFactory(string connectionString) : ISystemDbConnectionFactory
{
    public async Task<DbConnection> CreateAsync() => await SqliteConcreteConnectionFactory.Create(connectionString);
}
