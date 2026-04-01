using Autofac;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;
using Odin.Notarius.Database.Connection;

namespace Odin.Notarius.Database;

public static class NotaryExtensions
{
    public static ContainerBuilder AddSqliteNotaryDatabaseServices(this ContainerBuilder cb, string databasePath)
    {
        cb.RegisterNotaryDatabase();

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

        cb.Register(builder => new SqliteNotaryDbConnectionFactory(
                connectionString,
                builder.Resolve<IDbConnectionPool>()))
            .As<INotaryDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }

    //

    public static ContainerBuilder AddPgsqlNotaryDatabaseServices(this ContainerBuilder cb, string connectionString)
    {
        cb.RegisterNotaryDatabase();

        cb.Register(_ => new PgsqlNotaryDbConnectionFactory(connectionString))
            .As<INotaryDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }

    //

    private static ContainerBuilder RegisterNotaryDatabase(this ContainerBuilder cb)
    {
        // Database
        cb.RegisterType<Notarius.Database.NotaryDatabase>().InstancePerDependency();

        // Migrator
        cb.RegisterType<NotaryMigrator>().InstancePerDependency();

        // Connection
        cb.RegisterType<ScopedNotaryConnectionFactory>()
            .InstancePerLifetimeScope(); // Important!

        // Transaction
        cb.RegisterType<ScopedNotaryTransactionFactory>()
            .InstancePerLifetimeScope(); // Important!

        // Tables
        foreach (var tableType in Notarius.Database.NotaryDatabase.TableTypes)
        {
            cb.RegisterType(tableType).InstancePerLifetimeScope();
        }

        return cb;
    }
    
}