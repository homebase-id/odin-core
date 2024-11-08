using System.Data.Common;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Connection.Engine;

namespace Odin.Core.Storage.Database.Connection.Identity;

#nullable enable

public class PgsqlIdentityDbConnectionFactory(string connectionString) : IIdentityDbConnectionFactory
{
    public async Task<DbConnection> CreateAsync() => await PgsqlConcreteConnectionFactory.Create(connectionString);
}