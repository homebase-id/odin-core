using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.LogLevelOverwrite.Serilog;
using Odin.Core.Serialization;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
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
        foreach (var tenant in tenants)
        {
            ProcessTenant(odinConfig, tenant);
        }
    }
    
    //

    /*
     * for hver record R i DriveMainIndexRecord
         load og parse json header H identificeret ved FileId
         update R med relevante felter fra H
     *
     * DATABASE
     * DriveMainIndexRecord:
     * ...
     * hdrEncryptedKeyHeader => ServerFileHeader.EncryptedKeyHeader (json serialized)
     * hdrVersionTag => ServerFileHeader.FileMetadata.VersionTag
     * hdrAppData => ServerFileHeader.FileMetadata.AppData (json serialized)
     * hdrReactionSummary => ServerFileHeader.FileMetadata.ReactionPreview? UNSURE, ASK TODD
     * hdrServerData => ServerFileHeader.ServerMetadata (json serialized)
     * hdrTransferHistory => ServerFileHeader.ServerMetadata.TransferHistory
     * hdrFileMetaData => ServerFileHeader.FileMetadata (json serialized)
     * hdrTmpDriveAlias => ASK TODD
     * hdrTmpDriveType => ASK TODD
     *
     */

    private static void ProcessTenant(OdinConfiguration odinConfig, IdentityRegistration tenant)
    {
        Log.Information(tenant.PrimaryDomainName);
        var tenantHome = Path.Combine(odinConfig.Host.TenantDataRootPath, "registrations", tenant.Id.ToString());
        var headerFiles = Directory.GetFiles(tenantHome, "*.header", SearchOption.AllDirectories);
        foreach (var headerFile in headerFiles)
        {
            // 0323d2a3-053d-43d7-a968-c9cc0eba7bc8 / headers  / drives / 574d1ebf19e645108bd294db1670a49b / files / 3f72 / 24 / 19 / 20 / 3f72241920a2870055db058c2021ca26.header
            // tenant id                            /  dir     / dir    / ?                                / dir   / ?    / ?  / ?  / ?  / header filename
            Log.Information(headerFile);
            var json = File.ReadAllText(headerFile);
            var header = OdinSystemSerializer.Deserialize<ServerFileHeader>(json);
            Log.Information(header.EncryptedKeyHeader.ToString() ?? string.Empty);

            // ServerFileHeader

            // Per tenant
            var mgr = await GetLongTermStorageManager(header.FileMetadata.File.DriveId, db);

            // public Task SaveFileHeader(ServerFileHeader header, IdentityDatabase db)
            // Task SaveTransferHistory(Guid fileId, RecipientTransferHistory history, IdentityDatabase db);
            // Task SaveReactionSummary(Guid fileId, ReactionSummary summary, IdentityDatabase db);

        }

    }
    
}