using Autofac;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.Identity;

public static class IdentityExtensions
{
    public static ContainerBuilder AddSqliteIdentityDatabaseServices(this ContainerBuilder cb, string databasePath)
    {
        cb.RegisterIdentityCommon();

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

    public static ContainerBuilder AddPgsqlIdentityDatabaseServices(this ContainerBuilder cb, string connectionString)
    {
        cb.RegisterIdentityCommon();
        
        cb.Register(_ => new PgsqlIdentityDbConnectionFactory(connectionString))
            .As<IIdentityDbConnectionFactory>()
            .SingleInstance();

        return cb;
    }
    
    //

    private static ContainerBuilder RegisterIdentityCommon(this ContainerBuilder cb)
    {
        // Connection
        cb.RegisterType<ScopedIdentityConnectionFactory>().InstancePerLifetimeScope();
        
        // Tables
        cb.RegisterType<TableAppGrantsCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableAppNotificationsCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableCircleCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableCircleMemberCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableConnectionsCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableDriveAclIndexCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableDriveMainIndexCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableDriveReactionsCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableDriveTagIndexCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableFollowsMeCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableImFollowingCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableInboxCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableKeyThreeValueCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableKeyTwoValueCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableKeyUniqueThreeValueCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableKeyValueCRUD>().InstancePerLifetimeScope();
        cb.RegisterType<TableOutboxCRUD>().InstancePerLifetimeScope();
        
        return cb;
    }
    
    //
    
    
}