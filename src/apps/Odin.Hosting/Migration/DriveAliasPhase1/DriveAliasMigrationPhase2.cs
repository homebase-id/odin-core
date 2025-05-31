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
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;
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

            var newDriveManager = scope.Resolve<DriveManagerWithDedicatedTable>();

            var outputPath = $"{tenant.PrimaryDomainName}-drive-map.csv";
            await ExportDriveAliasMap(logger, tenantContext, newDriveManager, tenant, outputPath);
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

            await MoveData(logger, tenantContext, scope, tenant);
        }
    }

    private static async Task ExportDriveAliasMap(ILogger logger, TenantContext tenantContext,
        DriveManagerWithDedicatedTable newDriveManager, IdentityRegistration tenant, string outputPath)
    {
        var odinContext = CreateOdinContext(tenantContext);
        var allDrives = await newDriveManager.GetDrivesAsync(PageOptions.All, odinContext);

        var dotYouRegistryId = tenantContext.DotYouRegistryId;
        var tenantName = tenantContext.HostOdinId;
        await using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);

        await writer.WriteLineAsync("dotYouRegistryId,tenantName,driveId,driveAlias");

        foreach (var drive in allDrives.Results)
        {
            var oldDriveId = drive.Id;
            var driveAlias = drive.TargetDriveInfo.Alias.Value;
            await writer.WriteLineAsync($"\"{dotYouRegistryId}\",\"{tenantName}\",\"{oldDriveId}\",\"{driveAlias}\"");
        }

        logger.LogInformation("Drive completed for tenant {tenant}. Drive Count: {count}",
            tenant.PrimaryDomainName,
            allDrives.Results.Count);
    }

    private static async Task MoveData(ILogger logger, TenantContext tenantContext, ILifetimeScope scope, IdentityRegistration tenant)
    {
        var scopedIdentityTransactionFactory = scope.Resolve<ScopedIdentityTransactionFactory>();

        var newDriveManager = scope.Resolve<DriveManagerWithDedicatedTable>();

        await using var tx = await scopedIdentityTransactionFactory.BeginStackedTransactionAsync();
        var odinContext = CreateOdinContext(tenantContext);
        var allDrivesPage = await newDriveManager.GetDrivesAsync(PageOptions.All, odinContext);

        var t = scope.Resolve<TableDriveDefinitions>();

        var allDrives = allDrivesPage.Results.ToList();
        foreach (var drive in allDrives)
        {
            var oldDriveId = drive.Id;
            var driveAlias = drive.TargetDriveInfo.Alias.Value;

            await t.Temp_MigrateDriveMainIndex(oldDriveId, driveAlias);
            await t.Temp_MigrateDriveLocalTagIndex(oldDriveId, driveAlias);
            await t.Temp_MigrateDriveAclIndex(oldDriveId, driveAlias);
            await t.Temp_MigrateDriveTransferHistory(oldDriveId, driveAlias);
            await t.Temp_MigrateDriveReactions(oldDriveId, driveAlias);
            await t.Temp_MigrateDriveTagIndex(oldDriveId, driveAlias);

            await t.Temp_MigrateFollowsMe(oldDriveId, driveAlias);
            await t.Temp_MigrateImFollowing(oldDriveId, driveAlias);
            await t.Temp_MigrateInbox(oldDriveId, driveAlias);
            await t.Temp_MigrateOutbox(oldDriveId, driveAlias);

            await MigrateFileMetadata(oldDriveId, drive.TargetDriveInfo, scope);

            await t.Temp_MigrateDriveDefinitions(oldDriveId, driveAlias);
        }

        await MigrateCircleMembers(allDrives, scope);


        logger.LogInformation("Drive completed for tenant {tenant}. Drive Count: {count}",
            tenant.PrimaryDomainName,
            allDrives.Count);

        tx.Commit();
    }

    private static async Task MigrateFileMetadata(Guid oldDriveId, TargetDrive targetDrive, ILifetimeScope scope)
    {
        // get all files on this drive
        var index = scope.Resolve<TableDriveMainIndex>();
        var records = await index.GetAllByDriveIdAsync(oldDriveId);

        foreach (var record in records)
        {
            var header = ServerFileHeader.FromDriveMainIndexRecord(record);
            header.FileMetadata.File = header.FileMetadata.File with { DriveId = targetDrive.Alias };

            var updatedRecord = header.ToDriveMainIndexRecord(targetDrive);
            updatedRecord.driveId = targetDrive.Alias.Value;

            var count = await index.Temp_ResetDriveIdToAlias(updatedRecord);
            if (count != 1)
            {
                throw new OdinSystemException("Too many rows updated for filemetadata");
            }
        }
    }

    private static async Task MigrateCircleMembers(List<StorageDrive> drives, ILifetimeScope scope)
    {
        var circleMember = scope.Resolve<TableCircleMember>();

        var allCircleRecords = await circleMember.GetAllCirclesAsync();
        foreach (var record in allCircleRecords)
        {
            var data = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(record.data.ToStringFromUtf8Bytes());

            foreach (var driveGrant in data.CircleGrant.KeyStoreKeyEncryptedDriveGrants)
            {
                var theDrive = drives.SingleOrDefault(d => d.Id == driveGrant.DriveId);
                if (theDrive == null)
                {
                    throw new OdinSystemException($"Could not find drive with id {driveGrant.DriveId} in circle grant");
                }

                driveGrant.DriveId = theDrive.TargetDriveInfo.Alias;
            }
        }

        await circleMember.UpsertCircleMembersAsync(allCircleRecords);
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