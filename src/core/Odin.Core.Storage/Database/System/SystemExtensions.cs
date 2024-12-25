using System;
using Autofac;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System;

public static class SystemExtensions
{
    public static ContainerBuilder AddSqliteSystemDatabaseServices(this ContainerBuilder cb, string databasePath)
    {
        cb.RegisterSystemDatabase();

        cb.Register(builder => new DbConnectionPool(
                builder.Resolve<ILogger<DbConnectionPool>>(),
                Environment.ProcessorCount * 2))
            .As<IDbConnectionPool>()
            .SingleInstance();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private, // Shared is discouraged: https://www.sqlite.org/sharedcache.html
            Pooling = false // We use our own (DbConnectionPool)
        }.ToString();

        cb.Register(builder => new SqliteSystemDbConnectionFactory(
                connectionString,
                builder.Resolve<IDbConnectionPool>()))
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