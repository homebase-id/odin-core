using System;
using Autofac;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity;

#nullable enable

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
        // Database
        cb.RegisterType<IdentityDatabase>().InstancePerDependency();

        // Migrator
        cb.RegisterType<IdentityMigrator>().InstancePerDependency();

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

        // Caches
        cb.RegisterType<TableAppGrantsCached>().InstancePerLifetimeScope();
        cb.RegisterType<TableAppNotificationsCached>().InstancePerLifetimeScope();
        cb.RegisterType<TableCircleCached>().InstancePerLifetimeScope();
        cb.RegisterType<TableCircleMemberCached>().InstancePerLifetimeScope();
        cb.RegisterType<TableKeyValueCached>().InstancePerLifetimeScope();

        return cb;
    }
    
    //
    
    
}