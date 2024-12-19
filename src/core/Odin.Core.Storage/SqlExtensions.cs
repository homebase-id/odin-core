using System;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage;

public static class SqlExtensions
{
    public static string ToSql(this byte[] bytes, DatabaseType databaseType)
    {
        return databaseType switch
        {
            DatabaseType.Sqlite => $"x'{Convert.ToHexString(bytes)}'",
            DatabaseType.Postgres => $"'\\x{Convert.ToHexString(bytes)}'",
            _ => throw new NotSupportedException($"Database type {databaseType} not supported")
        };
    }

    public static string BytesToSql(this Guid guid, DatabaseType databaseType)
    {
        return guid.ToByteArray().ToSql(databaseType);
    }
}