using Autofac;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Database.Connection;
using Odin.Core.Storage.Database.Connection.Engine;
using Odin.Core.Storage.Database.Connection.System;

namespace Odin.Core.Storage.Database;

public static class DatabaseExtensions
{

    public static ContainerBuilder AddCommonDatabaseServices(this ContainerBuilder cb)
    {
        cb.RegisterType<ScopedSystemConnectionFactory>().InstancePerLifetimeScope();
        return cb;
    }
    
    // SQLite
    public static ContainerBuilder AddSqliteSystemDatabaseServices(this ContainerBuilder cb, string databasePath)
    {
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
                return new SqliteSystemDbConnectionFactory(connectionString);
            })
            .As<ISystemDbConnectionFactory>()
            .InstancePerLifetimeScope();

        return cb;
    }
    
    // PostgreSQL
    public static ContainerBuilder AddPgsqlSystemDatabaseServices(this ContainerBuilder cb, string connectionString)
    {
        cb.Register(_ => new PgsqlSystemDbConnectionFactory(connectionString))
            .As<ISystemDbConnectionFactory>()
            .SingleInstance();
       
        return cb;
    }
}    
