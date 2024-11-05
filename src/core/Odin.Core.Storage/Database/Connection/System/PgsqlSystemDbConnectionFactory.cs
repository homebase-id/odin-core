using System.Data.Common;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Connection.Engine;

namespace Odin.Core.Storage.Database.Connection.System;

#nullable enable

public class PgsqlSystemDbConnectionFactory(string connectionString) : ISystemDbConnectionFactory
{
    public async Task<DbConnection> CreateAsync() => await PgsqlConcreteConnectionFactory.Create(connectionString);
}