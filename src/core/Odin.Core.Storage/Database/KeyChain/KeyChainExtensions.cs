using System;
using Autofac;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.KeyChain.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.KeyChain;

public static class KeyChainExtensions
{
    public static ContainerBuilder AddSqliteKeyChainDatabaseServices(this ContainerBuilder cb, string databasePath)
    {
        cb.RegisterKeyChainDatabase();

        cb.Register(builder => new DbConnectionPool(
                builder.Resolve<ILogger<DbConnectionPool>>(),
                builder.Resolve<DatabaseCounters>(),
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

        cb.Register(builder => new SqliteKeyChainDbConnectionFactory(
                connectionString,
                builder.Resolve<IDbConnectionPool>()))
            .As<IKeyChainDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }

    //

    public static ContainerBuilder AddPgsqlKeyChainDatabaseServices(this ContainerBuilder cb, string connectionString)
    {
        cb.RegisterKeyChainDatabase();
        
        cb.Register(_ => new PgsqlKeyChainDbConnectionFactory(connectionString))
            .As<IKeyChainDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }
    
    //
    
    private static ContainerBuilder RegisterKeyChainDatabase(this ContainerBuilder cb)
    {
        // Database
        cb.RegisterType<KeyChainDatabase>().InstancePerDependency();

        // Connection
        cb.RegisterType<ScopedKeyChainConnectionFactory>()
            .InstancePerLifetimeScope(); // Important!

        // Transaction
        cb.RegisterType<ScopedKeyChainTransactionFactory>()
            .InstancePerLifetimeScope(); // Important!

        // Tables
        foreach (var tableType in KeyChainDatabase.TableTypes)
        {
            cb.RegisterType(tableType).InstancePerLifetimeScope();
        }

        return cb;
    }
    
}