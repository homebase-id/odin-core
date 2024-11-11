using Autofac;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.System;

public static class SystemExtensions
{
    public static ContainerBuilder AddSqliteSystemDatabaseServices(this ContainerBuilder cb, string databasePath)
    {
        cb.RegisterSystemCommon();
        
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
            .InstancePerLifetimeScope(); // Per scope so the pool boy can do his job when scope exists

        return cb;
    }

    //

    public static ContainerBuilder AddPgsqlSystemDatabaseServices(this ContainerBuilder cb, string connectionString)
    {
        cb.RegisterSystemCommon();
        
        cb.Register(_ => new PgsqlSystemDbConnectionFactory(connectionString))
            .As<ISystemDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }
    
    //
    
    private static ContainerBuilder RegisterSystemCommon(this ContainerBuilder cb)
    {
        // Connection
        cb.RegisterType<ScopedSystemConnectionFactory>().InstancePerLifetimeScope();
        
        // Tables
        cb.RegisterType<TableJobsCRUD>().InstancePerLifetimeScope();

        return cb;
    }
    
}