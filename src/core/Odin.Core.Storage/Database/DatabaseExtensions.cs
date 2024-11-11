using Autofac;
using Odin.Core.Storage.SQLite;

namespace Odin.Core.Storage.Database;

public enum DatabaseType
{
    Sqlite,
    Postgres
}

public static class DatabaseExtensions
{
    //
    // Common database services for all databases databases
    //

    // Note: be sure to also call this in any sub-scopes if isolation is required
    public static ContainerBuilder AddDatabaseCacheServices(this ContainerBuilder cb)
    {
        cb.RegisterType<CacheHelper>().SingleInstance();
        return cb;
    }
}
