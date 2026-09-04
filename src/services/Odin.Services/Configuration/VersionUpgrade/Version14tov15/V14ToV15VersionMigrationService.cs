using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.Configuration.VersionUpgrade.Version14tov15
{
    /// <summary>
    /// v14 -&gt; v15: mints the write-only keypair for every drive that predates it.
    /// </summary>
    /// <remarks>
    /// <see cref="DriveManager.CreateDriveAsync"/> now mints one at creation, so only drives made before
    /// that need filling in.  The keypair is what lets a caller with no prior relationship deposit to a
    /// drive: they fetch the public half and seal to it, and only a holder of the drive's storage key can
    /// unseal (docs/drive-addressing.md).
    ///
    /// <para>
    /// The backfill runs here rather than lazily because the caller who most needs the public half is a
    /// stranger fetching it over peer, and that request carries no grant on the drive -- so nothing in it
    /// can reach the storage key the private half is escrowed under.  Minting has to happen somewhere the
    /// master key is present, which is this pass and drive creation.  Same shape as the v11 -&gt; v12
    /// backfill of the connection-level keypair.
    /// </para>
    ///
    /// <para>
    /// Additive and repeatable: a drive that already has a keypair is skipped, never replaced.  Replacing
    /// one would strand every deposit already sealed to the old public half.
    /// </para>
    /// </remarks>
    public class V14ToV15VersionMigrationService(
        ILogger<V14ToV15VersionMigrationService> logger,
        DriveManager driveManager)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            var drives = await driveManager.GetDrivesAsync(PageOptions.All, odinContext);

            var minted = 0;
            foreach (var drive in drives.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await driveManager.EnsureWriteOnlyKeyPairAsync(drive.Id, odinContext))
                {
                    minted++;
                }
            }

            logger.LogInformation("v14->v15: minted a write-only keypair for {minted} of {total} drives",
                minted, drives.Results.Count);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var drives = await driveManager.GetDrivesAsync(PageOptions.All, odinContext);

            var missing = drives.Results.Where(d => d.WriteOnlyKeyPair == null).ToList();
            if (missing.Count != 0)
            {
                throw new OdinSystemException(
                    $"v14->v15: {missing.Count} drive(s) still have no write-only keypair; " +
                    $"first is {missing[0].Id} ({missing[0].Name})");
            }
        }
    }
}
