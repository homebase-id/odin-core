using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.Configuration.VersionUpgrade.Version21tov22
{
    /// <summary>
    /// v21 → v22: the WebDrop drive blocks enumeration, as its built-in definition now says
    /// (<see cref="Apps.Builtin.BuiltinDrives.WebDropDrive"/>).  A drop is reached by the unique id its link carries;
    /// until now anyone could list the drive, and so read or expire every drop on it.  A drive with WebDrop's alias
    /// but another type is someone else's and is left alone.
    /// </summary>
    public class V21ToV22VersionMigrationService(
        ILogger<V21ToV22VersionMigrationService> logger,
        DriveManager driveManager)
    {
        public async Task BlockWebDropEnumerationAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var drive = await WebDropDriveAsync();
            if (drive == null || drive.BlockEnumeration)
            {
                return;
            }

            logger.LogInformation("Blocking enumeration of the WebDrop drive {driveId}", drive.Id);
            await driveManager.SetSystemDriveBlockEnumerationAsync(drive.Id, true, odinContext);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var drive = await WebDropDriveAsync();
            if (drive is { BlockEnumeration: false })
            {
                throw new OdinSystemException($"The WebDrop drive {drive.Id} can still be listed");
            }
        }

        private async Task<StorageDrive> WebDropDriveAsync()
        {
            var webDrop = WellKnownAppDrives.WebDropDrive;
            var drive = await driveManager.GetDriveAsync(webDrop.Alias);
            return drive?.TargetDriveInfo.Type == webDrop.Type ? drive : null;
        }
    }
}
