using System;
using Autofac;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity;

public static class IdentityExtensions
{
    public static ContainerBuilder AddSqliteIdentityDatabaseServices(this ContainerBuilder cb, Guid identityId, string databasePath)
    {
        cb.RegisterIdentityDatabase(identityId);

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
      
        cb.Register(builder => new SqliteIdentityDbConnectionFactory(
                connectionString,
                builder.Resolve<IDbConnectionPool>()))
            .As<IIdentityDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }

    //

    public static ContainerBuilder AddPgsqlIdentityDatabaseServices(this ContainerBuilder cb, Guid identityId, string connectionString)
    {
        cb.RegisterIdentityDatabase(identityId);
        
        cb.Register(_ => new PgsqlIdentityDbConnectionFactory(connectionString))
            .As<IIdentityDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }
    
    //

    private static ContainerBuilder RegisterIdentityDatabase(this ContainerBuilder cb, Guid identityId)
    {
        // IdentityKey
        cb.RegisterInstance(new IdentityKey(identityId)).SingleInstance();

        // Database
        cb.RegisterType<IdentityDatabase>().InstancePerDependency();

        // Connection
        cb.RegisterType<ScopedIdentityConnectionFactory>()
            .InstancePerLifetimeScope(); // Important!

        // Transaction
        cb.RegisterType<ScopedIdentityTransactionFactory>()
            .InstancePerLifetimeScope(); // Important!

        // Tables
        foreach (var tableType in IdentityDatabase.TableTypes)
        {
            cb.RegisterType(tableType)
                .WithParameter(new TypedParameter(typeof(Guid), identityId))
                .InstancePerLifetimeScope();
        }

        // Abstractions
        cb.RegisterType<MainIndexMeta>().InstancePerLifetimeScope();
        cb.RegisterType<LocalMetadataDataOperations>().InstancePerLifetimeScope();
        
        return cb;
    }
    
    //
    
    
}