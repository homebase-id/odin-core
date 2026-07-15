using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version9tov10
{
    /// <summary>
    /// v9 → v10: ensures the system connection circles
    /// (<see cref="SystemCircleConstants.ConfirmedConnectionsCircleId"/> and
    /// <see cref="SystemCircleConstants.AutoConnectionsCircleId"/>) grant
    /// <see cref="DrivePermission.Read"/> on the <see cref="SystemDriveConstants.ProfileDrive"/>, and
    /// re-issues that grant to every already-connected identity.
    ///
    /// <para>
    /// The point of the grant is the <b>storage key</b>: a Read grant is what causes
    /// <see cref="DriveGrant.KeyStoreKeyEncryptedStorageKey"/> to be populated (the drive's storage key,
    /// re-encrypted under the member's key-store key), which is what lets the connected identity
    /// <i>decrypt</i> ProfileDrive data — not merely see the permission flag. Re-issuing the circle grant
    /// regenerates that encrypted storage key from the drive for each member.
    /// </para>
    ///
    /// <para>
    /// The ProfileDrive is an <c>AllowAnonymousReads</c> drive, so a fresh install already receives this
    /// grant automatically: <see cref="CircleNetworkService.HandleDriveAdded"/> adds a Read grant for
    /// every anonymous drive to both system circles when the drive is created. This migration backfills
    /// the same grant onto <b>existing</b> installs whose already-connected identities hold a circle-grant
    /// snapshot that predates it (and therefore lacks the storage key). It adds the ProfileDrive Read
    /// grant to each system circle definition if missing and calls
    /// <see cref="CircleNetworkService.UpdateCircleDefinitionAsync"/>, which both persists the definition
    /// and re-creates the circle grant — including the encrypted storage key — for every connected member.
    /// </para>
    /// </summary>
    public class V9ToV10VersionMigrationService(
        ILogger<V9ToV10VersionMigrationService> logger,
        CircleNetworkService circleNetworkService,
        CircleDefinitionService circleDefinitionService,
        IdentityDatabase db,
        IDriveManager driveManager)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            // The ProfileDrive is created up front by the EnsureSystemDrivesExist pre-pass in
            // VersionUpgradeService, so it exists before the circle-definition grant is validated below.

            await using var tx = await db.BeginStackedTransactionAsync(IsolationLevel.Unspecified, cancellationToken);

            foreach (var circleId in SystemCircleConstants.AllSystemCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureProfileDriveReadGrantAsync(circleId, odinContext);
            }

            tx.Commit();
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var profileDrive = await driveManager.GetDriveAsync(SystemDriveConstants.ProfileDrive.Alias, false);
            if (profileDrive == null)
            {
                throw new OdinSystemException("Profile drive not created");
            }

            // Every system circle definition must now grant Read on the ProfileDrive.
            foreach (var circleId in SystemCircleConstants.AllSystemCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var def = await circleDefinitionService.GetCircleAsync(circleId);
                if (def == null)
                {
                    throw new OdinSystemException($"System circle {circleId} not found");
                }

                if (!HasProfileDriveRead(def))
                {
                    throw new OdinSystemException($"System circle {circleId} does not grant Read on the ProfileDrive");
                }
            }

            // Every connected identity who is a member of a system circle must now hold the ProfileDrive grant.
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);
            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var circleId in SystemCircleConstants.AllSystemCircles)
                {
                    // Only validate identities who are members of this system circle.
                    if (!identity.PeerKeyStore.CircleGrants.TryGetValue(circleId, out var circleGrant))
                    {
                        continue;
                    }

                    var driveGrant = circleGrant.KeyStoreKeyEncryptedDriveGrants
                        .SingleOrDefault(g => g.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive);

                    if (driveGrant == null)
                    {
                        throw new OdinSystemException("Drive grant for ProfileDrive not found");
                    }

                    if (!driveGrant.PermissionedDrive.Permission.HasFlag(DrivePermission.Read))
                    {
                        throw new OdinSystemException("ProfileDrive not granted read permission");
                    }

                    // The Read grant is only meaningful if it carries the encrypted storage key — that's
                    // what lets the connected identity decrypt ProfileDrive data.
                    if (driveGrant.KeyStoreKeyEncryptedStorageKey == null)
                    {
                        throw new OdinSystemException(
                            $"ProfileDrive grant for identity {identity.OdinId} has no storage key; cannot decrypt drive data");
                    }
                }
            }
        }

        /// <summary>
        /// Adds the ProfileDrive Read grant to the given system circle definition if it is missing, then
        /// re-issues the circle grant to every connected member via
        /// <see cref="CircleNetworkService.UpdateCircleDefinitionAsync"/>. Calling it when the grant is
        /// already present simply re-propagates the (unchanged) definition to members, so existing
        /// identities whose snapshot predates the grant pick it up.
        /// </summary>
        private async Task EnsureProfileDriveReadGrantAsync(GuidId circleId, IOdinContext odinContext)
        {
            var def = await circleDefinitionService.GetCircleAsync(circleId);
            if (def == null)
            {
                // EnsureSystemCirclesExistAsync runs as part of provisioning; a missing system circle here
                // means the tenant isn't initialized. Nothing to backfill — skip rather than crash.
                logger.LogWarning("System circle {circleId} not found; skipping ProfileDrive grant backfill", circleId);
                return;
            }

            if (!HasProfileDriveRead(def))
            {
                logger.LogInformation("Granting ProfileDrive Read to system circle {circleId}", circleId);

                var grants = def.DriveGrants?.ToList() ?? new List<DriveGrantRequest>();
                grants.Add(new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ProfileDrive,
                        Permission = DrivePermission.Read
                    }
                });
                def.DriveGrants = grants;
            }

            // Persists the definition and re-creates the circle grant for every connected member.
            logger.LogDebug("Re-issuing system circle {circleId} grants to connected members", circleId);
            await circleNetworkService.UpdateCircleDefinitionAsync(def, odinContext);
        }

        private static bool HasProfileDriveRead(CircleDefinition def)
        {
            return def.DriveGrants?.Any(g =>
                g.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive &&
                g.PermissionedDrive.Permission.HasFlag(DrivePermission.Read)) ?? false;
        }
    }
}
