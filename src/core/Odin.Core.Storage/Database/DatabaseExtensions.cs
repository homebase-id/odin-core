using Autofac;

namespace Odin.Core.Storage.Database;

public static class DatabaseExtensions
{
    //
    // Common database services for all databases databases
    //

    // Note: be sure to also call this in any sub-scopes if isolation is required
    public static ContainerBuilder AddDatabaseCacheServices(this ContainerBuilder cb)
    {
        cb.RegisterInstance(new CacheHelper("database"));
        return cb;
    }

    // Note: be sure to also call this in any sub-scopes if isolation is required
    public static ContainerBuilder AddDatabaseCounterServices(this ContainerBuilder cb)
    {
        cb.RegisterType<DatabaseCounters>().SingleInstance();
        return cb;
    }

}
