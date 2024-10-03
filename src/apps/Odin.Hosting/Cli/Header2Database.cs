using System;
using System.Collections.Generic;
using System.IO;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.LogLevelOverwrite.Serilog;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Storage.SQLite.Migrations;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;
using Serilog;

namespace Odin.Hosting.Cli;

public static class Header2Database
{
    private static IServiceProvider _serviceProvider;
    
    //
    public static void Execute(IEnumerable<string> args)
    {
        DapperExtensions.ConfigureTypeHandlers();
        
        Environment.SetEnvironmentVariable("Job__SystemJobsEnabled", "false");
        Environment.SetEnvironmentVariable("Job__TenantJobsEnabled", "false");
        var (odinConfig, appConfig) = AppSettings.LoadConfig(true);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(appConfig)
            .WriteTo.LogLevelModifier(s => s.Async(sink => sink.Console())).CreateLogger();
        
        var startup = new Startup(appConfig, args);
        
        var services = new ServiceCollection();
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog();
        });
        
        startup.ConfigureServices(services);

        var serviceProviderFactory = new MultiTenantServiceProviderFactory(
            TenantServices.ConfigureMultiTenantServices,
            TenantServices.InitializeTenant);

        var builder = serviceProviderFactory.CreateBuilder(services);
        startup.ConfigureContainer(builder);
        
        _serviceProvider = serviceProviderFactory.CreateServiceProvider(builder);
        
        var idregs = _serviceProvider.GetRequiredService<IIdentityRegistry>();

        var tenants = idregs.GetTenants().Result;

        // Backup tenant databases
        foreach (var tenant in tenants)
        {
            BackupTenantDatabase(odinConfig, tenant);
        }
        
        // Migrate tenant databases and headers
        foreach (var tenant in tenants)
        {
            ProcessTenant(odinConfig, tenant);
        }
    }
    
    //

    private static void BackupTenantDatabase(OdinConfiguration odinConfig, IdentityRegistration registration)
    {
        Log.Information("Backup tenant {tenant}", registration.PrimaryDomainName);
        
        var tenantHome = Path.Combine(odinConfig.Host.TenantDataRootPath, "registrations", registration.Id.ToString());
        
        var identityDbPath = Path.Combine(tenantHome, "headers", "identity.db");
        if (!File.Exists(identityDbPath))
        {
            throw new Exception("Database not found: " + identityDbPath);
        }

        var identityBackupDbPath = Path.Combine(tenantHome, "headers", "identity-backup.db");
        if (File.Exists(identityBackupDbPath))
        {
            throw new Exception("Database backup exists: " + identityBackupDbPath);
        }
        
        BackupSqliteDatabase.Execute(identityDbPath, identityBackupDbPath);
    }
    
    //
   
    private static void ProcessTenant(OdinConfiguration odinConfig, IdentityRegistration registration)
    {
        Log.Information("ProcessTenant {tenant}", registration.PrimaryDomainName);
        
        var tenant = registration.PrimaryDomainName;
        var tenantContainer = _serviceProvider.GetRequiredService<IMultiTenantContainerAccessor>().Container();
        var tenantScope = tenantContainer.GetTenantScope(tenant);
        var tenantSystemStorage = tenantScope.Resolve<TenantSystemStorage>(); 
        var db = tenantSystemStorage.IdentityDatabase;
        var driveDatabaseHost = tenantScope.Resolve<DriveDatabaseHost>();
        var tenantHome = Path.Combine(odinConfig.Host.TenantDataRootPath, "registrations", registration.Id.ToString());
        
        //
        // Load all tblDriveMainIndex records into memory
        // Only process header files that are in that list
        //
        var driveMainIndexes = db.tblDriveMainIndex.GetAll().Result;
        
        db.tblDriveMainIndex.RecreateTable();
        
        foreach (var driveMainIndex in driveMainIndexes)
        {
            var driveId = driveMainIndex.driveId.ToString("N"); 
            var fileId = driveMainIndex.fileId.ToString("N");
            
            var headerPath = Path.Combine(
                tenantHome, 
                "headers", 
                "drives",
                driveId,
                "files",
                fileId.Substring(0, 4), 
                fileId.Substring(4, 2), 
                fileId.Substring(6, 2), 
                fileId.Substring(8, 2), 
                fileId + ".header");
            
            Log.Information("Loading header {headerPath}", headerPath);
            var json = File.ReadAllText(headerPath);
            var header = OdinSystemSerializer.DeserializeOrThrow<ServerFileHeader>(json);
            
            File.Move(headerPath, headerPath + ".backup");
            
            var sqliteDatabaseManager = driveDatabaseHost.TryGetOrLoadQueryManager(header.FileMetadata.File.DriveId, db).Result;
            sqliteDatabaseManager.SaveFileHeader(header, db).Wait();
            sqliteDatabaseManager.SaveTransferHistory(header.FileMetadata.File.FileId, header.ServerMetadata.TransferHistory, db).Wait();
            sqliteDatabaseManager.SaveReactionSummary(header.FileMetadata.File.FileId, header.FileMetadata.ReactionPreview, db).Wait();
        }
    }
}