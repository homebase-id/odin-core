using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Standard;

namespace Odin.Services.Configuration.VersionUpgrade.Version4tov5
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V4ToV5VersionMigrationService(
        ILogger<V4ToV5VersionMigrationService> logger,
        StandardFileSystem standardFileSystem,
        FollowerService followerService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();
            await DeleteAllTheThingsOnFeed(odinContext, cancellationToken);
            await ResyncTheFeedYaaaay(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            // not sure what to validate here...
            await Task.CompletedTask;
        }

        private async Task ResyncTheFeedYaaaay(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var peopleIFollow = await followerService.GetIdentitiesIFollowAsync(Int32.MaxValue, "", odinContext);
            foreach (var identity in peopleIFollow.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await followerService.SynchronizeChannelFilesAsync((OdinId)identity, odinContext);
            }
        }

        private async Task DeleteAllTheThingsOnFeed(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var feedDriveId = SystemDriveConstants.FeedDrive.Alias;

            var allTheThings = new FileQueryParams()
            {
                TargetDrive = SystemDriveConstants.FeedDrive
            };

            var options = new QueryBatchResultOptions
            {
                MaxRecords = Int32.MaxValue,
                IncludeHeaderContent = false,
                ExcludePreviewThumbnail = true,
                ExcludeServerMetaData = true,
                IncludeTransferHistory = false,
                Cursor = null,
                Ordering = QueryBatchSortOrder.Default,
                Sorting = QueryBatchSortField.FileId,
            };

            var allTheFiles = await standardFileSystem.Query.GetBatch(feedDriveId, allTheThings, options, odinContext);

            if (allTheFiles.HasMoreRows)
            {
                throw new OdinSystemException("Failed to upgrade.  Not all files returned from the feed drive query");
            }

            foreach (var searchResult in allTheFiles.SearchResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = new InternalDriveFileId
                {
                    DriveId = feedDriveId,
                    FileId = searchResult.FileId
                };

                await standardFileSystem.Storage.HardDeleteLongTermFile(file, odinContext);
                logger.LogDebug("{upgradeVersion} is deleting {file}", nameof(V4ToV5VersionMigrationService), file.FileId);

                var theShouldBeDeletedFile = await standardFileSystem.Storage.GetServerFileHeader(file, odinContext);
                if (theShouldBeDeletedFile != null)
                {
                    throw new OdinSystemException($"Failed to delete file {file.FileId}");
                }
            }

            var queryAgain = await standardFileSystem.Query.GetBatch(feedDriveId, allTheThings, options, odinContext);

            if (queryAgain.SearchResults.Any())
            {
                throw new OdinSystemException("Failed to upgrade. The feed still has files");
            }
        }
    }
}