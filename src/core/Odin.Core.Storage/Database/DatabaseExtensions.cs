using Autofac;

namespace Odin.Core.Storage.Database;

public static class DatabaseExtensions
{
    //
    // Common database services for all databases databases
    //

    // Note: be sure to also call this in any sub-scopes if isolation is required
    public static ContainerBuilder AddDatabaseServices(this ContainerBuilder cb)
    {
        cb.RegisterType<DatabaseCounters>().SingleInstance();
        return cb;
    }

}
