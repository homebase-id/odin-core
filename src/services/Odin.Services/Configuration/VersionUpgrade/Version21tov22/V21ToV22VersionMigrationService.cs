using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.Configuration.VersionUpgrade.Version21tov22
{
    /// <summary>
    /// v21 -&gt; v22: lets the CDN read every public channel drive (#1681).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AllowCdn</c> is opt-in per drive, and only Public Posts is seeded with it.  A channel drive made before the
    /// flag existed, or since without it, is left out of the CDN's grants (<c>CdnAuthPathHandler</c>), though a
    /// channel's content is what the CDN is for.
    /// </para>
    /// <para>
    /// Only channel drives that allow anonymous reads: the CDN reads as <c>System</c> and so passes every file's
    /// ACL (docs/file-access.md), so turning it on for a connections-only channel would hand the CDN files the
    /// owner shared with connections only.  An anonymous drive is reachable by anyone anyway
    /// (docs/security-model.md, the drive options), so nothing new is exposed.  A zero-knowledge drive never
    /// allows anonymous reads, so none is picked.
    /// </para>
    /// <para>
    /// Turn-on only: re-running it changes nothing, and a drive the owner turns the CDN off on later stays off,
    /// since this runs once per identity.
    /// </para>
    /// </remarks>
    public class V21ToV22VersionMigrationService(
        ILogger<V21ToV22VersionMigrationService> logger,
        IDriveManager driveManager)
    {
        public async Task EnableCdnOnPublicChannelDrivesAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            var drives = await PublicChannelDrivesWithoutCdnAsync(odinContext);
            foreach (var drive in drives)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await driveManager.SetDriveAllowCdnAsync(drive.Id, true, odinContext);
                logger.LogDebug("v21->v22: the CDN may now read channel drive {drive}", drive.Name);
            }

            logger.LogInformation("v21->v22: turned the CDN on for {count} public channel drive(s)", drives.Count);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var left = await PublicChannelDrivesWithoutCdnAsync(odinContext);
            if (left.Count != 0)
            {
                throw new OdinSystemException(
                    $"v21->v22 left {left.Count} public channel drive(s) without the CDN: {string.Join(", ", left.Select(d => d.Name))}");
            }
        }

        private async Task<List<StorageDrive>> PublicChannelDrivesWithoutCdnAsync(IOdinContext odinContext)
        {
            var channels = await driveManager.GetDrivesAsync(SystemDriveConstants.ChannelDriveType, PageOptions.All, odinContext);
            return channels.Results.Where(d => d.AllowAnonymousReads && !d.IsCdnEnabled()).ToList();
        }
    }
}
