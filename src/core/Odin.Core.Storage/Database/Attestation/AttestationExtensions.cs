using System;
using Autofac;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Attestation.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Attestation;

public static class AttestationExtensions
{
    public static ContainerBuilder AddSqliteAttestationDatabaseServices(this ContainerBuilder cb, string databasePath)
    {
        cb.RegisterAttestationDatabase();

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

        cb.Register(builder => new SqliteAttestationDbConnectionFactory(
                connectionString,
                builder.Resolve<IDbConnectionPool>()))
            .As<IAttestationDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }

    //

    public static ContainerBuilder AddPgsqlAttestationDatabaseServices(this ContainerBuilder cb, string connectionString)
    {
        cb.RegisterAttestationDatabase();
        
        cb.Register(_ => new PgsqlAttestationDbConnectionFactory(connectionString))
            .As<IAttestationDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }
    
    //
    
    private static ContainerBuilder RegisterAttestationDatabase(this ContainerBuilder cb)
    {
        // Database
        cb.RegisterType<AttestationDatabase>().InstancePerDependency();

        // Migrator
        cb.RegisterType<AbstractMigrator>().InstancePerDependency();

        // Connection
        cb.RegisterType<ScopedAttestationConnectionFactory>()
            .InstancePerLifetimeScope(); // Important!

        // Transaction
        cb.RegisterType<ScopedAttestationTransactionFactory>()
            .InstancePerLifetimeScope(); // Important!

        // Tables
        foreach (var tableType in AttestationDatabase.TableTypes)
        {
            cb.RegisterType(tableType).InstancePerLifetimeScope();
        }

        return cb;
    }
    
}