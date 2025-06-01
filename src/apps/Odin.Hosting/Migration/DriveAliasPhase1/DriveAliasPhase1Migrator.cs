using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Migration.DriveAliasPhase1;

public static class DriveAliasPhase1Migrator
{
    public static async Task MigrateDrives(IIdentityRegistry registry, MultiTenantContainer tenantContainer, ILogger logger)
    {
        var allTenants = await registry.GetTenants();

        logger.LogInformation("Migrating drive alias phase 1 data - tenant count: {tenants}", allTenants.Count);
        foreach (var tenant in allTenants)
        {
            logger.LogInformation("Drive Migration started for tenant {tenant}", tenant.PrimaryDomainName);
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tenantContext = scope.Resolve<TenantContext>();
            var scopedIdentityTransactionFactory = scope.Resolve<ScopedIdentityTransactionFactory>();

            var odinContext = CreateOdinContext(tenantContext);
            await using var tx = await scopedIdentityTransactionFactory.BeginStackedTransactionAsync();
            
            var successfullyMigrated = await MigrateDriveDefinitions(logger, odinContext, tenant.PrimaryDomainName, scope);
            
            logger.LogInformation("Drive completed for tenant {tenant}. Drive Count: {count}", tenant.PrimaryDomainName,
                successfullyMigrated);
            
            tx.Commit();
        }

        logger.LogInformation("Successfully migrated drive alias phase 1 data");
    }

    private static async Task<int> MigrateDriveDefinitions(ILogger logger, OdinContext odinContext, string tenantPrimaryDomain,
        ILifetimeScope scope)
    {
        
        // cleanup
        
        var t = scope.Resolve<TableDrives>();
        await t.Temp_Truncate();
        
        var newDriveManager = scope.Resolve<DriveManagerWithDedicatedTable>();
        var count = await newDriveManager.MigrateDrivesFromClassDriveManager();

        //
        // validate all drives are exactly copied
        //

        var oldDriveManger = scope.Resolve<DriveManagerWithDedicatedTable>();
        var oldDrivesPage = await oldDriveManger.GetDrivesAsync(PageOptions.All, odinContext);
        var newDrivesPage = await newDriveManager.GetDrivesAsync(PageOptions.All, odinContext);

        var result = StorageDriveComparer.CompareLists(oldDrivesPage.Results.ToList(), newDrivesPage.Results.ToList());
        if (result.OnlyInFirst.Any() || result.OnlyInSecond.Any() || result.Mismatched.Any())
        {
            foreach (var d in result.OnlyInFirst)
                logger.LogError($"Only in first list: {d.Id}");

            foreach (var d in result.OnlyInSecond)
                logger.LogError($"Only in second list: {d.Id}");

            foreach (var (d1, d2, diffs) in result.Mismatched)
            {
                logger.LogError($"Mismatched ID {d1.Id}:");
                foreach (var diff in diffs)
                    logger.LogError($"  - {diff}");
            }

            throw new OdinSystemException($"Failure during drive migration for tenant {tenantPrimaryDomain}");
        }

        return count;
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