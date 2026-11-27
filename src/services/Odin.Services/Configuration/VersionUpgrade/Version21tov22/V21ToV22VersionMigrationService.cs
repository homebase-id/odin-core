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
    /// v21 → v22: every WebDrop drive stores <c>BlockEnumeration = true</c> (#2136).
    /// </summary>
    /// <remarks>
    /// A WebDrop drive made before the option existed already blocks enumeration: a system drive has what its built-in
    /// definition says (<see cref="StorageDrive.BlockEnumeration"/>).  But its stored value is still off, so anything that
    /// reads the stored details directly -- a report, a backup, an export -- sees a drive anyone may list.  Every drive of
    /// WebDrop's type, the system one included, now stores it, as the issue asks.  For the system drive that only records
    /// what it does; an owner-made drive of the type stops being listable by anyone but the owner, its apps and the
    /// server.  Except one that allows subscriptions, which can't
    /// block enumeration (<c>DriveManager.AssertListingFits</c>): storing it would throw and fail the upgrade on every
    /// attempt, so that drive is left as it is and logged.
    /// </remarks>
    public class V21ToV22VersionMigrationService(
        ILogger<V21ToV22VersionMigrationService> logger,
        DriveManager driveManager)
    {
        public async Task StoreWebDropBlockEnumerationAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            foreach (var drive in await WebDropDrivesStoredListableAsync(odinContext))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (drive.AllowSubscriptions)
                {
                    logger.LogWarning("Leaving the WebDrop drive {driveId} listable: it allows subscriptions", drive.Id);
                    continue;
                }

                logger.LogInformation("Storing BlockEnumeration on the WebDrop drive {driveId}", drive.Id);
                await driveManager.StoreBlockEnumerationAsync(drive.Id, true, odinContext);
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var listable = (await WebDropDrivesStoredListableAsync(odinContext)).Where(d => !d.AllowSubscriptions).ToList();
            if (listable.Count > 0)
            {
                throw new OdinSystemException(
                    $"WebDrop drive(s) {string.Join(", ", listable.Select(d => d.Id))} still store BlockEnumeration off");
            }
        }

        private async Task<List<StorageDrive>> WebDropDrivesStoredListableAsync(IOdinContext odinContext)
        {
            var drives = await driveManager.GetDrivesAsync(WellKnownAppDrives.WebDropDrive.Type, PageOptions.All, odinContext);
            return drives.Results.Where(d => !d.Data.BlockEnumeration).ToList();
        }
    }
}
