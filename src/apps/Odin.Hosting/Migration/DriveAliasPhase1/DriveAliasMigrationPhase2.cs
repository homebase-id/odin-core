using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Migration.DriveAliasPhase1;

public static class DriveAliasMigrationPhase2
{
    public static async Task ExportMap(IIdentityRegistry registry, MultiTenantContainer tenantContainer, ILogger logger)
    {
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            logger.LogInformation("Drive Migration Phase 2 - Started for tenant {tenant}", tenant.PrimaryDomainName);
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tenantContext = scope.Resolve<TenantContext>();

            var scopedIdentityTransactionFactory = scope.Resolve<ScopedIdentityTransactionFactory>();
            var newDriveManager = scope.Resolve<DriveManagerWithDedicatedTable>();

            var outputPath = $"{tenant.PrimaryDomainName}-drive-map.csv";
            await ExportDriveAliasMap(logger, scopedIdentityTransactionFactory, tenantContext, newDriveManager, tenant, outputPath);
            logger.LogInformation($"{tenant} map written to {outputPath}", tenant.PrimaryDomainName, outputPath);
        }
    }

    public static async Task MigrateData(IIdentityRegistry registry, MultiTenantContainer tenantContainer, ILogger logger)
    {
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            logger.LogInformation("Drive Migration Phase 2 - Started for tenant {tenant}", tenant.PrimaryDomainName);
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tenantContext = scope.Resolve<TenantContext>();

            var scopedIdentityTransactionFactory = scope.Resolve<ScopedIdentityTransactionFactory>();
            var newDriveManager = scope.Resolve<DriveManagerWithDedicatedTable>();

            await MoveData(logger, scopedIdentityTransactionFactory, tenantContext, newDriveManager, scope, tenant);
        }
    }

    private static async Task ExportDriveAliasMap(ILogger logger, ScopedIdentityTransactionFactory scopedIdentityTransactionFactory,
        TenantContext tenantContext, DriveManagerWithDedicatedTable newDriveManager, IdentityRegistration tenant, string outputPath)
    {
        await using var tx = await scopedIdentityTransactionFactory.BeginStackedTransactionAsync();
        var odinContext = CreateOdinContext(tenantContext);
        var allDrives = await newDriveManager.GetDrivesAsync(PageOptions.All, odinContext);

        // only change folders after all drive updates succeed
        foreach (var drive in allDrives.Results)
        {
            var oldDriveId = drive.Id;
            var driveAlias = drive.TargetDriveInfo.Alias.Value;
            WriteRenameCsv(tenantContext, oldDriveId, driveAlias, outputPath);
        }

        logger.LogInformation("Drive completed for tenant {tenant}. Drive Count: {count}",
            tenant.PrimaryDomainName,
            allDrives.Results.Count);

        tx.Commit();
    }

    private static async Task MoveData(ILogger logger, ScopedIdentityTransactionFactory scopedIdentityTransactionFactory,
        TenantContext tenantContext, DriveManagerWithDedicatedTable newDriveManager, ILifetimeScope scope, IdentityRegistration tenant)
    {
        await using var tx = await scopedIdentityTransactionFactory.BeginStackedTransactionAsync();
        var odinContext = CreateOdinContext(tenantContext);
        var allDrives = await newDriveManager.GetDrivesAsync(PageOptions.All, odinContext);

        var t = scope.Resolve<TableDriveDefinitions>();

        foreach (var drive in allDrives.Results)
        {
            var oldDriveId = drive.Id;
            var driveAlias = drive.TargetDriveInfo.Alias.Value;

            await t.Temp_MigrateDriveMainIndex(oldDriveId, driveAlias);
            await t.Temp_MigrateDriveLocalTagIndex(oldDriveId, driveAlias);
            await t.Temp_MigrateDriveAclIndex(oldDriveId, driveAlias);
            await t.Temp_MigrateDriveTransferHistory(oldDriveId, driveAlias);
            await t.Temp_MigrateDriveReactions(oldDriveId, driveAlias);
            await t.Temp_MigrateDriveDefinitions(oldDriveId, driveAlias);
        }

        // only change folders after all drive updates succeed
        // foreach (var drive in allDrives.Results)
        // {
        //     var oldDriveId = drive.Id;
        //     var driveAlias = drive.TargetDriveInfo.Alias.Value;
        //
        //     RenameFolders(tenantContext, oldDriveId, driveAlias);
        // }

        logger.LogInformation("Drive completed for tenant {tenant}. Drive Count: {count}",
            tenant.PrimaryDomainName,
            allDrives.Results.Count);

        tx.Commit();
    }

    private static void RenameFolders(TenantContext tenantContext, Guid oldDriveId, Guid driveAlias)
    {
        var pathManager = tenantContext.TenantPathManager;

        // payloads
        var oldFolderPath = pathManager.GetDrivePayloadPath(oldDriveId).Replace(TenantPathManager.FilesFolder, "");
        var newFolderPath = pathManager.GetDrivePayloadPath(driveAlias).Replace(TenantPathManager.FilesFolder, "");
        EnsureMoved(oldFolderPath, newFolderPath);

        var oldUploadFolder = pathManager.GetDriveUploadPath(oldDriveId).Replace(TenantPathManager.UploadFolder, "");
        var newUploadFolder = pathManager.GetDriveUploadPath(driveAlias).Replace(TenantPathManager.UploadFolder, "");
        EnsureMoved(oldUploadFolder, newUploadFolder);

        var oldInboxFolder = pathManager.GetDriveInboxPath(oldDriveId).Replace(TenantPathManager.InboxFolder, "");
        var newInboxFolder = pathManager.GetDriveInboxPath(driveAlias).Replace(TenantPathManager.InboxFolder, "");
        EnsureMoved(oldInboxFolder, newInboxFolder);

        void EnsureMoved(string oldPath, string newPath)
        {
            if (Directory.Exists(newPath))
            {
                // skip new folder
                return;
            }

            if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath!);
            }
        }
    }


    private static void WriteRenameCsv(TenantContext tenantContext, Guid oldDriveId, Guid driveAlias, string outputCsvPath)
    {
        var pathManager = tenantContext.TenantPathManager;
        var dotYouRegistryId = tenantContext.DotYouRegistryId;
        var tenantName = tenantContext.HostOdinId;

        var entries = new List<(string Source, string Target)>();

        AddMoveEntry(pathManager.GetDrivePayloadPath(oldDriveId), pathManager.GetDrivePayloadPath(driveAlias),
            TenantPathManager.FilesFolder);
        AddMoveEntry(pathManager.GetDriveUploadPath(oldDriveId), pathManager.GetDriveUploadPath(driveAlias),
            TenantPathManager.UploadFolder);
        AddMoveEntry(pathManager.GetDriveInboxPath(oldDriveId), pathManager.GetDriveInboxPath(driveAlias), TenantPathManager.InboxFolder);

        // Write to CSV
        using var writer = new StreamWriter(outputCsvPath, false, Encoding.UTF8);
        writer.WriteLine("dotYouRegistryId,tenantName,sourceFolder,targetFolder");

        foreach (var (source, target) in entries)
        {
            writer.WriteLine($"\"{dotYouRegistryId}\",\"{tenantName}\",\"{source}\",\"{target}\"");
        }

        void AddMoveEntry(string oldPathFull, string newPathFull, string folderBase)
        {
            var oldPath = oldPathFull.Replace(folderBase, "");
            var newPath = newPathFull.Replace(folderBase, "");

            if (Directory.Exists(oldPath))
            {
                entries.Add((oldPath, newPath));
            }
        }
    }


    private static OdinContext CreateOdinContext(TenantContext tenantContext)
    {
        var context = new OdinContext
        {
            Tenant = tenantContext.HostOdinId,
            AuthTokenCreated = null,
            Caller = new CallerContext(
                odinId: (OdinId)"system.domain",
                masterKey: null,
                securityLevel: SecurityGroupType.System,
                circleIds: null,
                tokenType: ClientTokenType.Other)
        };

        context.SetPermissionContext(new PermissionContext(null, null, true));
        return context;
    }
}