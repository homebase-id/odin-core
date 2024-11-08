using System.Data.Common;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.Identity.Connection;

#nullable enable

public class SqliteIdentityDbConnectionFactory(string connectionString) : IIdentityDbConnectionFactory
{
    public async Task<DbConnection> CreateAsync() => await SqliteConcreteConnectionFactory.Create(connectionString);
}
