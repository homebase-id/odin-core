using System;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage;

public static class GuidCast
{
    public static object Cast(this Guid guid, DatabaseType databaseType)
    {
        return databaseType switch
        {
            DatabaseType.Sqlite => guid,
            DatabaseType.Postgres => guid,
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
        };
    }

    public static object Cast(this Guid? guid, DatabaseType databaseType)
    {
        return guid == null ? DBNull.Value : guid.Value.Cast(databaseType);
    }
}
