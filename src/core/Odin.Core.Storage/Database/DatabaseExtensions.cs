using Autofac;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Database.Connection;
using Odin.Core.Storage.Database.Connection.Engine;
using Odin.Core.Storage.Database.Connection.Identity;
using Odin.Core.Storage.Database.Connection.System;
using Odin.Core.Storage.SQLite;

namespace Odin.Core.Storage.Database;

public static class DatabaseExtensions
{
    //
    // Common database services for system and tenant databases
    //

    // NOTE: Call this for both system and per-tenant databases
    // Single instance services in tenant scope will override the root instance ones
    public static ContainerBuilder AddCommonDatabaseServices(this ContainerBuilder cb)
    {
        cb.RegisterType<CacheHelper>().SingleInstance();
        cb.RegisterType<ScopedSystemConnectionFactory>().InstancePerLifetimeScope();
        cb.RegisterType<ScopedIdentityConnectionFactory>().InstancePerLifetimeScope();
        return cb;
    }

    //
    // SQLite specifics
    //

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
            .InstancePerLifetimeScope(); // Per scope so the pool boy can do his job when scope exists

        return cb;
    }

    public static ContainerBuilder AddSqliteIdentityDatabaseServices(this ContainerBuilder cb, string databasePath)
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
                return new SqliteIdentityDbConnectionFactory(connectionString);
            })
            .As<IIdentityDbConnectionFactory>()
            .InstancePerLifetimeScope(); // Per scope so the pool boy can do his job when scope exists

        return cb;
    }

    //
    // Postgres specifics
    //

    public static ContainerBuilder AddPgsqlDatabaseServices(this ContainerBuilder cb, string connectionString)
    {
        cb.Register(_ => new PgsqlSystemDbConnectionFactory(connectionString))
            .As<ISystemDbConnectionFactory>()
            .SingleInstance();

        cb.Register(_ => new PgsqlIdentityDbConnectionFactory(connectionString))
            .As<IIdentityDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }

}
