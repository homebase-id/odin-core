using Autofac;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.Identity;

public static class IdentityExtensions
{
    public static ContainerBuilder AddSqliteIdentityDatabaseServices(this ContainerBuilder cb, string databasePath)
    {
        cb.RegisterIdentityDatabase();

        cb.RegisterType<SqlitePoolBoy>().InstancePerLifetimeScope();

        cb.Register(c =>
            {
                c.Resolve<SqlitePoolBoy>(); // this makes sure pool boy goes to work when the scope is disposed
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared,
                    Pooling = true
                }.ToString();
                return new SqliteIdentityDbConnectionFactory(connectionString);
            })
            .As<IIdentityDbConnectionFactory>()
            .InstancePerLifetimeScope(); // Per scope so the pool boy can do his job when scope exists

        return cb;
    }

    //

    public static ContainerBuilder AddPgsqlIdentityDatabaseServices(this ContainerBuilder cb, string connectionString)
    {
        cb.RegisterIdentityDatabase();
        
        cb.Register(_ => new PgsqlIdentityDbConnectionFactory(connectionString))
            .As<IIdentityDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }
    
    //

    private static ContainerBuilder RegisterIdentityDatabase(this ContainerBuilder cb)
    {
        // Database
        cb.RegisterType<IdentityDatabase>().InstancePerDependency();

        // Connection
        cb.RegisterType<ScopedIdentityConnectionFactory>()
            .InstancePerLifetimeScope(); // Important!
        
        // Tables
        foreach (var tableType in IdentityDatabase.TableTypes)
        {
            cb.RegisterType(tableType).InstancePerDependency();
        }
        
        return cb;
    }
    
    //
    
    
}