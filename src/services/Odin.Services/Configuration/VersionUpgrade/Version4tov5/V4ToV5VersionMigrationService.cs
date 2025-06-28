using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;

namespace Odin.Services.Configuration.VersionUpgrade.Version4tov5
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V4ToV5VersionMigrationService(
        ILogger<V4ToV5VersionMigrationService> logger,
        FollowerService followerService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogDebug("Repopulating the feed");
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
                try
                {
                    logger.LogDebug("Start: Synchronizing channels from {identity}", identity);
                    await followerService.SynchronizeChannelFilesAsync((OdinId)identity, odinContext);
                    logger.LogDebug("Done: Synchronizing channels from {identity}", identity);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed syncing {identity}.", identity);
                }
            }
        }
    }
}