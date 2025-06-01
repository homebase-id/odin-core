using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Migration.DriveAliasPhase1;

public static class DriveAliasMigrationPhaseThree
{
    public static async Task UpdateFileSystem(IIdentityRegistry registry, MultiTenantContainer tenantContainer, ILogger logger)
    {
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            logger.LogInformation("Drive Migration Phase Three - Started for tenant {tenant}", tenant.PrimaryDomainName);
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tenantContext = scope.Resolve<TenantContext>();

            var newDriveManager = scope.Resolve<DriveManagerWithDedicatedTable>();

            var odinContext = CreateOdinContext(tenantContext);
            var allDrives = await newDriveManager.GetDrivesAsync(PageOptions.All, odinContext);

            // only change folders after all drive updates succeed
            foreach (var drive in allDrives.Results)
            {
                var oldDriveId = drive.TempOriginalDriveId;
                var driveAlias = drive.TargetDriveInfo.Alias.Value;
                
                RenameFolders(tenantContext, oldDriveId, driveAlias);
            }

            logger.LogInformation("Drive completed for tenant {tenant}. Drive Count: {count}",
                tenant.PrimaryDomainName,
                allDrives.Results.Count);

        }
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