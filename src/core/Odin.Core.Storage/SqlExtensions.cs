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

    public static string SqlNowString(DatabaseType dbType)
    {
        if (dbType == DatabaseType.Sqlite)
            return "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
        else
            return "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
    }

    public static string MaxString(DatabaseType dbType)
    {
        if (dbType == DatabaseType.Sqlite)
            return "MAX";
        else
            return "GREATEST";
    }
}