using Autofac;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Database.System.Connection;

namespace Odin.Core.Storage.Database.System;

public static class SystemExtensions
{
    public static ContainerBuilder AddSqliteSystemDatabaseServices(this ContainerBuilder cb, string databasePath)
    {
        cb.RegisterSystemDatabase();
        
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        cb.Register(_ => new SqliteSystemDbConnectionFactory(connectionString))
            .As<ISystemDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }

    //

    public static ContainerBuilder AddPgsqlSystemDatabaseServices(this ContainerBuilder cb, string connectionString)
    {
        cb.RegisterSystemDatabase();
        
        cb.Register(_ => new PgsqlSystemDbConnectionFactory(connectionString))
            .As<ISystemDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }
    
    //
    
    private static ContainerBuilder RegisterSystemDatabase(this ContainerBuilder cb)
    {
        // Database
        cb.RegisterType<SystemDatabase>().InstancePerDependency();

        // Connection
        cb.RegisterType<ScopedSystemConnectionFactory>()
            .InstancePerLifetimeScope(); // Important!

        // Transaction
        cb.RegisterType<ScopedSystemTransactionFactory>()
            .InstancePerLifetimeScope(); // Important!

        // Tables
        foreach (var tableType in SystemDatabase.TableTypes)
        {
            cb.RegisterType(tableType).InstancePerLifetimeScope();
        }

        return cb;
    }
    
}