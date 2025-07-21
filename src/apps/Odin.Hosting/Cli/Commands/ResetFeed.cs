using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Tasks;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

public static class ResetFeed
{
    internal static async Task ExecuteAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var tenantContainer = services.GetRequiredService<IMultiTenantContainer>();

        logger.LogInformation("Starting Feed reset");

        registry.LoadRegistrations().BlockingWait();
        var allTenants = await registry.GetTenants();
        var feedDriveId = SystemDriveConstants.FeedDrive.Alias;
        foreach (var tenant in allTenants)
        {
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var db = scope.Resolve<IdentityDatabase>();
            await using var tx = await db.BeginStackedTransactionAsync();

            var results = await GetHeadersInFeedDrive(db);

            var headers = results.Item1;
            logger.LogDebug("Deleting {items} headers from feed rive for tenant {t}", headers.Count, tenant.PrimaryDomainName);

            foreach (var header in headers)
            {
                // because im oddly paranoid
                if (header.driveId != SystemDriveConstants.FeedDrive.Alias)
                {
                    logger.LogError("whoa horsey, you're going to delete something not on the feed " +
                                    "drive.  the incorrect drive was {d}", header.driveId);
                    throw new OdinSystemException("invalid drive");
                }

                await db.MainIndexMeta.DeleteEntryAsync(feedDriveId, header.fileId);
            }

            var shouldBeAllGoneResults = await GetHeadersInFeedDrive(db);
            if (shouldBeAllGoneResults.Item1.Count > 0)
            {
                throw new OdinSystemException("items still on feed drive");
            }

            tx.Commit();
        }

        logger.LogInformation("Feed reset complete");

        async Task<(List<DriveMainIndexRecord>, bool moreRows, QueryBatchCursor cursor)> GetHeadersInFeedDrive(IdentityDatabase db)
        {
            var results = await db.MainIndexMeta.QueryBatchAsync(feedDriveId,
                Int32.MaxValue,
                null,
                QueryBatchSortOrder.Default,
                requiredSecurityGroup: new IntRange(0, (int)SecurityGroupType.Owner),
                fileSystemType: (int)FileSystemType.Standard);
            return results;
        }
    }
}