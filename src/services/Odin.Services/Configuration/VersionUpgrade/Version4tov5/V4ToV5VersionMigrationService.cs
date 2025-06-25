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
        FollowerService followerService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();
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

    }
}