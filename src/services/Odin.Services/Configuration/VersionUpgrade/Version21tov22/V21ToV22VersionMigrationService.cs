using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Apps.Builtin;
using Odin.Services.Drives.Management;

namespace Odin.Services.Configuration.VersionUpgrade.Version21tov22
{
    /// <summary>
    /// v21 → v22 (#2136): every WebDrop drive stores <c>BlockEnumeration = true</c>, and a Filesync or Photos drive the
    /// owner made by installing the app is reset to its built-in definition.
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
    /// <para>
    /// Filesync and Photos are system drives now, so an owner can no longer change one's settings or archive state.  One
    /// the owner made by installing the app, before, keeps whatever it was given -- archived, say, for good.  It is
    /// unarchived and given its definition's settings, as a new identity's drive has them (decided on #2136).
    /// </para>
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

        public async Task ResetInstalledFilesyncAndPhotosDrivesAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            foreach (var (drive, _) in await InstalledDrivesAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogInformation("Resetting the drive {driveId} to its built-in definition", drive.Id);
                await driveManager.ResetToBuiltinDefinitionAsync(drive.Id, odinContext);
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var unreset = (await InstalledDrivesAsync()).Where(d => !BuiltinDrives.MatchesDefinition(d.Drive, d.Definition)).ToList();
            if (unreset.Count > 0)
            {
                throw new OdinSystemException(
                    $"Drive(s) {string.Join(", ", unreset.Select(d => d.Drive.Id))} don't match their built-in definition");
            }

            var listable = (await WebDropDrivesStoredListableAsync(odinContext)).Where(d => !d.AllowSubscriptions).ToList();
            if (listable.Count > 0)
            {
                throw new OdinSystemException(
                    $"WebDrop drive(s) {string.Join(", ", listable.Select(d => d.Id))} still store BlockEnumeration off");
            }
        }

        // The Filesync and Photos drives this identity has: the owner's browser makes them on a new identity, so before
        // this version only an install did
        private async Task<List<(StorageDrive Drive, CreateDriveRequest Definition)>> InstalledDrivesAsync()
        {
            var installed = new List<(StorageDrive, CreateDriveRequest)>();
            foreach (var target in new[] { WellKnownAppDrives.FilesyncDrive, WellKnownAppDrives.PhotobaseDrive })
            {
                var drive = await driveManager.GetDriveAsync(target.Alias);
                if (drive != null && drive.TargetDriveInfo == target)
                {
                    installed.Add((drive, BuiltinDrives.DefinitionOf(target)));
                }
            }

            return installed;
        }

        private async Task<List<StorageDrive>> WebDropDrivesStoredListableAsync(IOdinContext odinContext)
        {
            var drives = await driveManager.GetDrivesAsync(WellKnownAppDrives.WebDropDrive.Type, PageOptions.All, odinContext);
            return drives.Results.Where(d => !d.Data.BlockEnumeration).ToList();
        }
    }
}
