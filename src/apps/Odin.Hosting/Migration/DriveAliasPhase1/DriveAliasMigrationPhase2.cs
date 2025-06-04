using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Factory;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Migration.DriveAliasPhase1;

public static class DriveAliasMigrationPhase2
{
    public static async Task ExportMap(IIdentityRegistry registry, MultiTenantContainer tenantContainer, ILogger logger,
        string exportPath)
    {
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            logger.LogInformation("Drive Migration Phase 2 - Started for tenant {tenant}", tenant.PrimaryDomainName);
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tenantContext = scope.Resolve<TenantContext>();
            var oldDriveManager = scope.Resolve<DriveManager>();

            var folder = Path.Combine(exportPath, "export");
            Directory.CreateDirectory(folder);

            var outputPath = Path.Combine(folder, $"{tenant.PrimaryDomainName}-drive-map.csv");
            await ExportDriveAliasMap(logger, tenantContext, oldDriveManager, tenant, outputPath);
            logger.LogInformation($"{tenant} map written to {outputPath}", tenant.PrimaryDomainName, outputPath);
        }
    }

    public static async Task MigrateData(IIdentityRegistry registry, MultiTenantContainer tenantContainer, ILogger logger)
    {
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            logger.LogInformation("Drive Migration - Started for tenant {tenant}", tenant.PrimaryDomainName);
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tenantContext = scope.Resolve<TenantContext>();
            try
            {
                var scopedIdentityTransactionFactory = scope.Resolve<ScopedIdentityTransactionFactory>();
                await using var tx = await scopedIdentityTransactionFactory.BeginStackedTransactionAsync();
                await MoveData(logger, tenantContext, scope);

                tx.Commit();
                logger.LogInformation("Migration completed for tenant {tenant}", tenant.PrimaryDomainName);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed for tenant {t}", tenant);
            }
        }
    }

    private static async Task ExportDriveAliasMap(ILogger logger, TenantContext tenantContext,
        DriveManager oldDriveManager, IdentityRegistration tenant, string outputPath)
    {
        var odinContext = CreateOdinContext(tenantContext);
        var allDrives = await oldDriveManager.GetDrivesAsync(PageOptions.All, odinContext);

        var dotYouRegistryId = tenantContext.DotYouRegistryId;
        var tenantName = tenantContext.HostOdinId;
        await using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);

        await writer.WriteLineAsync("dotYouRegistryId,tenantName,driveName,driveId,driveAlias");

        foreach (var drive in allDrives.Results)
        {
            var oldDriveId = drive.Id;
            var driveAlias = drive.TargetDriveInfo.Alias.Value;
            await writer.WriteLineAsync($"\"{dotYouRegistryId}\",\"{tenantName}\",\"{drive.Name}\",\"{oldDriveId}\",\"{driveAlias}\"");
        }

        logger.LogInformation("Drive completed for tenant {tenant}. Drive Count: {count}",
            tenant.PrimaryDomainName,
            allDrives.Results.Count);
    }

    private static async Task MoveData(ILogger logger, TenantContext tenantContext, ILifetimeScope scope)
    {
        var newDriveManager = scope.Resolve<DriveManagerWithDedicatedTable>();
        var odinContext = CreateOdinContext(tenantContext);
        var allDrivesPage = await newDriveManager.GetDrivesAsync(PageOptions.All, odinContext);
        var allDrives = allDrivesPage.Results.ToList();

        if (!allDrives.Any())
        {
            var oldDriveManager = scope.Resolve<DriveManager>();
            var oldDrives = await oldDriveManager.GetDrivesAsync(PageOptions.All, odinContext);
            if (oldDrives.Results.Any())
            {
                throw new OdinSystemException("drives were not migrated to new Drives table");
            }

            logger.LogWarning("Tenant has no old drives; stopping");
            return;
        }

        var t = scope.Resolve<TableDrives>();

        foreach (var drive in allDrives)
        {
            var oldDriveId = drive.TempOriginalDriveId;
            var newDriveId = drive.Id;

            await t.Temp_MigrateDriveMainIndex(oldDriveId, newDriveId);
            await MigrateFileMetadata(drive.TargetDriveInfo, scope);

            await t.Temp_MigrateDriveLocalTagIndex(oldDriveId, newDriveId);
            await t.Temp_MigrateDriveAclIndex(oldDriveId, newDriveId);
            await t.Temp_MigrateDriveTransferHistory(oldDriveId, newDriveId);
            await t.Temp_MigrateDriveReactions(oldDriveId, newDriveId);
            await t.Temp_MigrateDriveTagIndex(oldDriveId, newDriveId);

            await t.Temp_MigrateFollowsMe(oldDriveId, newDriveId);
            await t.Temp_MigrateImFollowing(oldDriveId, newDriveId);
            await t.Temp_MigrateInbox(oldDriveId, newDriveId);
            await t.Temp_MigrateOutbox(oldDriveId, newDriveId);
        }

        await MigrateCircleMembers(allDrives, scope, logger);
        await MigrateAppRegistration(allDrives, scope);
        await MigrateConnections(allDrives, scope);

        await t.Temp_CleanupOldTables();
    }

    private static async Task MigrateFileMetadata(TargetDrive targetDrive, ILifetimeScope scope)
    {
        // get all files on this drive
        var index = scope.Resolve<TableDriveMainIndex>();
        var records = await index.GetAllByDriveIdAsync(targetDrive.Alias);

        foreach (var record in records)
        {
            var header = ServerFileHeader.FromDriveMainIndexRecord(record);
            header.FileMetadata.File = header.FileMetadata.File with { DriveId = targetDrive.Alias };

            try
            {
                var updatedRecord = header.ToDriveMainIndexRecord(targetDrive);
                var count = await index.Temp_ResetDriveIdToAlias(updatedRecord);
                if (count != 1)
                {
                    throw new OdinSystemException("Too many rows updated for filemetadata");
                }
            }
            catch (OdinDatabaseValidationException)
            {
                //gulp
            }
        }
    }

    private static async Task MigrateCircleMembers(List<StorageDrive> drives, ILifetimeScope scope, ILogger logger)
    {
        var circleMember = scope.Resolve<TableCircleMember>();

        var allCircleRecords = await circleMember.GetAllCirclesAsync();

        if (!allCircleRecords.Any())
        {
            logger.LogWarning("No circle records found; skipping");
            return;
        }

        foreach (var record in allCircleRecords)
        {
            var data = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(record.data.ToStringFromUtf8Bytes());

            foreach (var driveGrant in data.CircleGrant.KeyStoreKeyEncryptedDriveGrants)
            {
                if (driveGrant.DriveId == driveGrant.PermissionedDrive.Drive.Alias)
                {
                    // record already changed; this allows us to run this again (and again)
                    continue;
                }

                var theDrive = drives.SingleOrDefault(d => d.TempOriginalDriveId == driveGrant.DriveId);
                if (theDrive == null)
                {
                    throw new OdinSystemException($"Could not find drive with id {driveGrant.DriveId} in circle grant");
                }

                driveGrant.DriveId = theDrive.Id;
            }

            record.data = OdinSystemSerializer.Serialize(data).ToUtf8ByteArray();
        }

        await circleMember.UpsertCircleMembersAsync(allCircleRecords);
    }

    private static async Task MigrateAppGrants(List<StorageDrive> drives, ILifetimeScope scope)
    {
        await Task.CompletedTask;

        // var appGrants = scope.Resolve<TableAppGrants>();
        //
        //
        // var allCircleRecords = await circleMember.GetAllCirclesAsync();
        // foreach (var record in allCircleRecords)
        // {
        //     var data = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(record.data.ToStringFromUtf8Bytes());
        //
        //     foreach (var driveGrant in data.CircleGrant.KeyStoreKeyEncryptedDriveGrants)
        //     {
        //         var theDrive = drives.SingleOrDefault(d => d.TempOriginalDriveId == driveGrant.DriveId);
        //         if (theDrive == null)
        //         {
        //             throw new OdinSystemException($"Could not find drive with id {driveGrant.DriveId} in circle grant");
        //         }
        //
        //         driveGrant.DriveId = theDrive.Id;
        //     }
        //
        //     record.data = OdinSystemSerializer.Serialize(data).ToUtf8ByteArray();
        // }
        //
        // await circleMember.UpsertCircleMembersAsync(allCircleRecords);
    }

    private static async Task MigrateAppRegistration(List<StorageDrive> drives, ILifetimeScope scope)
    {
        // update the exchange grants for all registered apps
        var svc = scope.Resolve<AppRegistrationService>();
        await svc.Temp_ReconcileDrives();
    }

    private static async Task MigrateConnections(List<StorageDrive> drives, ILifetimeScope scope)
    {
        //IdentityConnectionRegistration
        await Task.CompletedTask;
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
                securityLevel: SecurityGroupType.Owner,
                circleIds: null,
                tokenType: ClientTokenType.Other)
        };

        context.SetPermissionContext(new PermissionContext(null, null, true));
        return context;
    }
}