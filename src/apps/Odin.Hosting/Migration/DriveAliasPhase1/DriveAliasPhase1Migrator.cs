using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Migration.DriveAliasPhase1;

public static class DriveAliasPhase1Migrator
{
    public static async Task MigrateData(IIdentityRegistry registry, MultiTenantContainer tenantContainer, ILogger logger)
    {
        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            logger.LogInformation("Drive Migration started for tenant {tenant}", tenant.PrimaryDomainName);
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var tenantContext = scope.Resolve<TenantContext>();
            var scopedIdentityTransactionFactory = scope.Resolve<ScopedIdentityTransactionFactory>();

            var newDriveManager = scope.Resolve<DriveManagerWithDedicatedTable>();

            await using var tx = await scopedIdentityTransactionFactory.BeginStackedTransactionAsync();
            var count = await newDriveManager.MigrateDrivesFromClassDriveManager();

            logger.LogInformation("Drive completed for tenant {tenant}. Drive Count: {count}", tenant.PrimaryDomainName, count);

            //
            // validate all drives are exactly copied
            //

            var odinContext = CreateOdinContext(tenantContext);
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

                throw new OdinSystemException($"Failure during drive migration for tenant {tenant.PrimaryDomainName}");
            }

            logger.LogInformation("Migrating drive success for {t}; all drives are equivalent", tenant.PrimaryDomainName);


            tx.Commit();
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