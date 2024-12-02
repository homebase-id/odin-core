using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.Identity.Connection;

#nullable enable

public sealed class SqliteIdentityDbConnectionFactory(string connectionString) : IIdentityDbConnectionFactory, IDisposable
{
    public async Task<DbConnection> CreateAsync() => await SqliteConcreteConnectionFactory.Create(connectionString);
    public void Dispose()
    {
        using var cn = new SqliteConnection(connectionString);
        SqliteConnection.ClearPool(cn);
    }
}
