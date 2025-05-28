using System;
using System.Diagnostics.Eventing.Reader;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage;

#nullable enable

public static class CommandWrapperExtensions
{
    public static string SqlNow(this ICommandWrapper commandWrapper) 
    { 
        return commandWrapper.DatabaseType 
            switch {
                DatabaseType.Sqlite => "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)",
                DatabaseType.Postgres => "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000",
                _ => throw new NotSupportedException($"Database type {commandWrapper.DatabaseType} not supported") 
            };
    }

    public static string SqlMax(this ICommandWrapper commandWrapper)
    {
        return commandWrapper.DatabaseType
            switch
        {
            DatabaseType.Sqlite => "MAX",
            DatabaseType.Postgres => "GREATEST",
            _ => throw new NotSupportedException($"Database type {commandWrapper.DatabaseType} not supported")
        };
    }
}

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
        else if (dbType == DatabaseType.Postgres)
            return "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
        else
            throw new OdinDatabaseException(DatabaseType.Unknown, "Incorrect database type");
    }

    public static string MaxString(DatabaseType dbType)
    {
        if (dbType == DatabaseType.Sqlite)
            return "MAX";
        else if (dbType == DatabaseType.Postgres)
            return "GREATEST";
        else
            throw new OdinDatabaseException(DatabaseType.Unknown, "Incorrect database type");
    }
}