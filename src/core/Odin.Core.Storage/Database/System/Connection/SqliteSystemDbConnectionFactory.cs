using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.System.Connection;

#nullable enable

public sealed class SqliteSystemDbConnectionFactory(string connectionString)
    : AbstractSqliteDbConnectionFactory(connectionString), ISystemDbConnectionFactory
{
}
