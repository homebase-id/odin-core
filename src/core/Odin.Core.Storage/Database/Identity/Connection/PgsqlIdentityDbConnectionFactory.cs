using System.Data.Common;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory.Pgsql;

namespace Odin.Core.Storage.Database.Identity.Connection;

#nullable enable

public class PgsqlIdentityDbConnectionFactory(string connectionString) : IIdentityDbConnectionFactory
{
    public async Task<DbConnection> CreateAsync() => await PgsqlConcreteConnectionFactory.Create(connectionString);
}