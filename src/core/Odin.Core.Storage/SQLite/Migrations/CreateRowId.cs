using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Odin.Core.Logging;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.SQLite.Migrations.Helpers;

namespace Odin.Core.Storage.SQLite.Migrations;

//
// MIGRATION steps
//
//  - Change to directory /identity-host
//  - Make sure container is stopped: docker compose down && docker container prune -f
//  - Build and deploy docker image with migration code - DO NOT START IT
//  - Change to directory /identity-host/data/
//  - Backup the system: sudo zip -r backup-system.zip system
//  - Change to directory /identity-host/data/tenants
//  - Backup the registrations: sudo zip -r backup-registrations.zip registrations
//  - Change to directory /identity-host
//  - Edit the docker-compose.yml file:
//    - Add the correct command line param to start the migration
//    - Disable start-always if enabled
//  - Start the docker image: docker compose up
//  - Wait for the migration to finish
//  - Make sure docker container is gone: docker container prune -f
//  - Redeploy the docker image (this will overwrite the compose changes from above) - START IT
//  - Run some smoke tests
//  - Check the logs for errors
//  - Change to directory /identity-host/data/
//  - Clean up: sudo rm backup-system.zip
//  - Change to directory /identity-host/data/tenants/registrations
//  - Clean up: sudo find . -type f -name 'oldidentity.*' -delete
//  - Clean up: sudo rm backup-registrations.zip
//
// ROLLBACK steps
//
//  - Change to directory /identity-host
//  - Make sure container is stopped: docker compose down && docker container prune -f
//  - Change to directory /identity-host/data/
//  - Remove system: sudo rm -rf system
//  - Restore system: sudo unzip backup-system.zip
//  - Change to directory /identity-host/data/tenants
//  - Remove registrations: sudo rm -rf registrations
//  - Restore registrations: sudo unzip backup-registrations.zip
//  - Redeploy the docker image (this will overwrite the compose changes) - START IT
//  - Change to directory /identity-host/data/
//  - Clean up: sudo rm backup-system.zip
//  - Change to directory /identity-host/data/tenants/registrations
//  - Clean up: sudo find . -type f -name 'oldidentity.*' -delete
//  - Clean up: sudo rm backup-registrations.zip
//

// Local test:
//   mkdir $HOME/tmp/create-rowid
//   rsync -rvz yagni.dk:/identity-host/data/system $HOME/tmp/create-rowid/data
//   rsync -rvz yagni.dk:/identity-host/data/tenants/registrations $HOME/tmp/create-rowid/data/tenants
// run params:
//   --create-rowid $HOME/tmp/create-rowid/data <--dryrun | --commit>

// PROD:
// run params:
//   --create-rowid /identity-host/data <--dryrun | --commit>

#if false

public class CreateRowId
{
    private static ILogger<CreateRowId> _logger = new NullLogger<CreateRowId>();

    public static async Task Execute(string dataRootPath, bool commit)
    {
        var systemDir = Path.Combine(dataRootPath, "system");
        await DoSystemDir(systemDir);

        var tenantDirs = Directory.GetDirectories(Path.Combine(dataRootPath, "tenants", "registrations"));
        foreach (var tenantDir in tenantDirs)
        {
            await DoTenantDir(Guid.Parse(Path.GetFileName(tenantDir)), tenantDir);
        }

        if (!commit)
        {
            _logger.LogInformation("Dry run - no changes committed");
            return;
        }

        CommitSystemDir(systemDir);
        foreach (var tenantDir in tenantDirs)
        {
            CommitTenantDir(tenantDir);
        }

        _logger.LogInformation("Changes committed");
    }

    //

    private static async Task DoSystemDir(string systemDir)
    {
        var dbPath = Path.Combine(systemDir, "sys.db");
        var srcDbPath = Path.Combine(systemDir, "src_sys.db");
        var dstDbPath = Path.Combine(systemDir, "dst_sys.db");

        if (!File.Exists(dbPath))
        {
            throw new Exception("Database not found: " + dbPath);
        }

        if (File.Exists(srcDbPath)) File.Delete(srcDbPath);
        if (File.Exists(dstDbPath)) File.Delete(dstDbPath);

        BackupSqliteDatabase.Execute(dbPath, srcDbPath);

        await DoSystem(srcDbPath, dstDbPath);
    }

    //

    private static async Task DoSystem(string srcDbPath, string dstDbPath)
    {
        var (srcServices, dstServices) = RegisterSystemServices(Guid.Empty, srcDbPath, dstDbPath);
        try
        {
            await using var srcScope = srcServices.BeginLifetimeScope();
            await using var dstScope = dstServices.BeginLifetimeScope();

            _logger = srcScope.Resolve<ILogger<CreateRowId>>();
            _logger.LogInformation("Starting migration: {srcDbPath} -> {dstDbPath}", srcDbPath, dstDbPath);

            await MapIt<TableJobsOldCRUD, JobsOldRecord, TableJobs, JobsRecord>(srcScope, dstScope);
        }
        finally
        {
            await srcServices.DisposeAsync();
            await dstServices.DisposeAsync();
        }
    }

    //

    private static void CommitSystemDir(string systemDir)
    {
        var patterns = new List<string>{ "*-shm", "*-wal" };
        var files = patterns.SelectMany(pattern => Directory.GetFiles(systemDir, pattern)).Distinct().ToArray();

        if (files.Length > 0)
        {
            throw new Exception("Database files not cleaned up: " + string.Join(", ", files));
        }

        var dbPath = Path.Combine(systemDir, "sys.db");
        var srcDbPath = Path.Combine(systemDir, "src_sys.db");
        var dstDbPath = Path.Combine(systemDir, "dst_sys.db");

        if (!File.Exists(dstDbPath))
        {
            throw new Exception("Database not found: " + dbPath);
        }

        SqliteJournalMode.SetDelete(dbPath);
        SqliteJournalMode.SetDelete(srcDbPath);
        SqliteJournalMode.SetDelete(dstDbPath);

        if (File.Exists(srcDbPath)) File.Delete(srcDbPath);
        if (File.Exists(dbPath)) File.Delete(dbPath);

        File.Move(dstDbPath, dbPath);
    }

    //

    private static async Task DoTenantDir(Guid tenantId, string tenantDir)
    {
        var dbPath = Path.Combine(tenantDir, "headers", "identity.db");
        var srcDbPath = Path.Combine(tenantDir, "headers", "src_identity.db");
        var dstDbPath = Path.Combine(tenantDir, "headers", "dst_identity.db");

        if (!File.Exists(dbPath))
        {
            throw new Exception("Database not found: " + dbPath);
        }

        if (File.Exists(srcDbPath)) File.Delete(srcDbPath);
        if (File.Exists(dstDbPath)) File.Delete(dstDbPath);

        BackupSqliteDatabase.Execute(dbPath, srcDbPath);

        await DoTenant(tenantId, srcDbPath, dstDbPath);
    }

    //

    private static async Task DoTenant(Guid tenantId, string srcDbPath, string dstDbPath)
    {
        var (srcServices, dstServices) = RegisterSystemServices(tenantId, srcDbPath, dstDbPath);
        try
        {
            await using var srcScope = srcServices.BeginLifetimeScope();
            await using var dstScope = dstServices.BeginLifetimeScope();

            _logger = srcScope.Resolve<ILogger<CreateRowId>>();
            _logger.LogInformation("Starting migration: {srcDbPath} -> {dstDbPath}", srcDbPath, dstDbPath);

            // null-guids in Inbox' fileId
            var srcScopedConnectionFactory = srcScope.Resolve<ScopedIdentityConnectionFactory>();
            await using var srcCn = await srcScopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = srcCn.CreateCommand();
            cmd.CommandText = "DELETE FROM inbox WHERE fileId = @fileId";
            cmd.Parameters.Add(new SqliteParameter("@fileId", DbType.Binary) { Value = Guid.Empty.ToByteArray() });
            await cmd.ExecuteNonQueryAsync();

            var dstScopedConnectionFactory = dstScope.Resolve<ScopedIdentityConnectionFactory>();
            await using var dstCn = await dstScopedConnectionFactory.CreateScopedConnectionAsync();
            await using var dstTx = await dstCn.BeginStackedTransactionAsync();

            await MapIt<TableAppGrantsOldCRUD, AppGrantsOldRecord, TableAppGrants, AppGrantsRecord>(srcScope, dstScope);
            await MapIt<TableAppNotificationsOldCRUD, AppNotificationsOldRecord, TableAppNotifications, AppNotificationsRecord>(srcScope, dstScope);
            await MapIt<TableCircleOldCRUD, CircleOldRecord, TableCircle, CircleRecord>(srcScope, dstScope);
            await MapIt<TableCircleMemberOldCRUD, CircleMemberOldRecord, TableCircleMember, CircleMemberRecord>(srcScope, dstScope);
            await MapIt<TableConnectionsOldCRUD, ConnectionsOldRecord, TableConnections, ConnectionsRecord>(srcScope, dstScope);
            await MapIt<TableDriveAclIndexOldCRUD, DriveAclIndexOldRecord, TableDriveAclIndex, DriveAclIndexRecord>(srcScope, dstScope);
            await MapIt<TableDriveLocalTagIndexOldCRUD, DriveLocalTagIndexOldRecord, TableDriveLocalTagIndex, DriveLocalTagIndexRecord>(srcScope, dstScope);
            await MapIt<TableDriveMainIndexOldCRUD, DriveMainIndexOldRecord, TableDriveMainIndex, DriveMainIndexRecord>(srcScope, dstScope);
            await MapIt<TableDriveReactionsOldCRUD, DriveReactionsOldRecord, TableDriveReactions, DriveReactionsRecord>(srcScope, dstScope);
            await MapIt<TableDriveTagIndexOldCRUD, DriveTagIndexOldRecord, TableDriveTagIndex, DriveTagIndexRecord>(srcScope, dstScope);
            await MapIt<TableDriveTransferHistoryOldCRUD, DriveTransferHistoryOldRecord, TableDriveTransferHistory, DriveTransferHistoryRecord>(srcScope, dstScope);
            await MapIt<TableFollowsMeOldCRUD, FollowsMeOldRecord, TableFollowsMe, FollowsMeRecord>(srcScope, dstScope);
            await MapIt<TableImFollowingOldCRUD, ImFollowingOldRecord, TableImFollowing, ImFollowingRecord>(srcScope, dstScope);
            await MapIt<TableInboxOldCRUD, InboxOldRecord, TableInbox, InboxRecord>(srcScope, dstScope);
            await MapIt<TableKeyThreeValueOldCRUD, KeyThreeValueOldRecord, TableKeyThreeValue, KeyThreeValueRecord>(srcScope, dstScope);
            await MapIt<TableKeyTwoValueOldCRUD, KeyTwoValueOldRecord, TableKeyTwoValue, KeyTwoValueRecord>(srcScope, dstScope);
            await MapIt<TableKeyUniqueThreeValueOldCRUD, KeyUniqueThreeValueOldRecord, TableKeyUniqueThreeValue, KeyUniqueThreeValueRecord>(srcScope, dstScope);
            await MapIt<TableKeyValueOldCRUD, KeyValueOldRecord, TableKeyValue, KeyValueRecord>(srcScope, dstScope);
            await MapIt<TableOutboxOldCRUD, OutboxOldRecord, TableOutbox, OutboxRecord>(srcScope, dstScope);

            dstTx.Commit();
        }
        finally
        {
            await srcServices.DisposeAsync();
            await dstServices.DisposeAsync();
        }
    }

    //

    private static void CommitTenantDir(string tenantDir)
    {
        var patterns = new List<string>{ "*-shm", "*-wal" };
        var files = patterns.SelectMany(pattern => Directory.GetFiles(tenantDir, pattern)).Distinct().ToArray();

        if (files.Length > 0)
        {
            throw new Exception("Database files not cleaned up: " + string.Join(", ", files));
        }

        var dbPath = Path.Combine(tenantDir, "headers", "identity.db");
        var srcDbPath = Path.Combine(tenantDir, "headers", "src_identity.db");
        var dstDbPath = Path.Combine(tenantDir, "headers", "dst_identity.db");

        if (!File.Exists(dstDbPath))
        {
            throw new Exception("Database not found: " + dbPath);
        }

        SqliteJournalMode.SetDelete(dbPath);
        SqliteJournalMode.SetDelete(srcDbPath);
        SqliteJournalMode.SetDelete(dstDbPath);

        if (File.Exists(srcDbPath)) File.Delete(srcDbPath);
        if (File.Exists(dbPath)) File.Delete(dbPath);

        File.Move(dstDbPath, dbPath);
    }

    //

    private static async Task MapIt<TSrcTable, TSrcRecord, TDstTable, TDstRecord>(ILifetimeScope srcScope, ILifetimeScope dstScope)
    {
        _logger.LogInformation("  {from} -> {to}", typeof(TSrcTable).Name, typeof(TDstTable).Name);

        var srcTable = srcScope.Resolve<TSrcTable>();
        var rename = typeof(TSrcTable).GetMethod("RenameAsync", BindingFlags.Public | BindingFlags.Instance);
        await (Task)rename!.Invoke(srcTable, null)!;

        var dstTable = dstScope.Resolve<TDstTable>();
        var ensureTableExists = typeof(TDstTable).GetMethod("EnsureTableExistsAsync", BindingFlags.Public | BindingFlags.Instance);
        await (Task)ensureTableExists!.Invoke(dstTable, [false])!;

        var getAll = typeof(TSrcTable).GetMethod("GetAllAsync", BindingFlags.Public | BindingFlags.Instance);
        var rows = await (Task<List<TSrcRecord>>)getAll!.Invoke(srcTable, null)!;

        var insert = typeof(TDstTable).GetMethod("InsertAsync", BindingFlags.Public | BindingFlags.Instance,
            null,
            [typeof(TDstRecord)],
            null) ?? throw new Exception("InsertAsync not found");

        var mapper = new MapperConfiguration(cfg => { cfg.CreateMap<TSrcRecord, TDstRecord>(); }).CreateMapper();

        foreach (var record in rows)
        {
            var dstRecord = mapper.Map<TDstRecord>(record);
            await ((Task)insert!.Invoke(dstTable, [dstRecord]))!;
        }

    }


    #region Service Hell

    public static (IContainer srcContainer, IContainer dstContainer) RegisterSystemServices(
        Guid identityId,
        string srcDatabasePath,
        string dstDatabasePath)
    {
        IContainer BuildContainer(string databasePath)
        {
            var services = new ServiceCollection();

            services.AddLogging(logging =>
            {
                logging
                    .AddConsole(options =>
                    {
                        options.FormatterName = "CustomFormatter";
                    })
                    .AddConsoleFormatter<CustomConsoleFormatter, ConsoleFormatterOptions>();
                logging.SetMinimumLevel(LogLevel.Debug);
            });

            var builder = new ContainerBuilder();
            builder.Populate(services);

            builder.RegisterModule(new LoggingAutofacModule());

            builder.AddDatabaseCacheServices();
            builder.AddDatabaseCounterServices();
            builder.AddSqliteSystemDatabaseServices(databasePath);
            builder.AddSqliteIdentityDatabaseServices(identityId, databasePath);

            // Register all the OLD CRUD tables (don't do this in real code!!!)
            builder.RegisterAssemblyTypes(typeof(CreateRowId).Assembly)
                .Where(t => t.Name.EndsWith("OldCRUD"))
                .AsSelf();

            var container = builder.Build();
            return container;
        }

        var srcContainer = BuildContainer(srcDatabasePath);
        var dstContainer = BuildContainer(dstDatabasePath);

        return (srcContainer, dstContainer);
    }

    //

    public class CustomConsoleFormatter : ConsoleFormatter
    {
        public CustomConsoleFormatter() : base("CustomFormatter") { }

        public override void Write<TState>(
            in LogEntry<TState> logEntry,
            IExternalScopeProvider scopeProvider,
            TextWriter textWriter)
        {
            var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
            if (string.IsNullOrEmpty(message))
                return;

            // Write only the log level and message
            textWriter.WriteLine($"{message}");
        }
    }

    #endregion
}

#endif