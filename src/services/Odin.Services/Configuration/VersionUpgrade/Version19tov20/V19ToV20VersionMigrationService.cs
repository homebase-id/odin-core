using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version19tov20
{
    /// <summary>
    /// v19 → v20: the first step of retiring the system connection circles (#1809).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Once the Confirmed Connections circle is gone, a connection reads anonymous drives through the
    /// ambient grant alone, which carries no storage key -- so an unreviewed connection only ever sees
    /// plaintext.  The key for encrypted, connection-only data now comes from the circles the owner puts a
    /// reviewed contact into:
    /// </para>
    /// <list type="bullet">
    /// <item>Family, Friends and Work grant Read on the ProfileDrive.</item>
    /// <item>Feed grants Read on the Public Posts channel drive.</item>
    /// </list>
    /// <para>
    /// A fresh install gets these from <see cref="BuiltinCircles"/>.  An existing one does not:
    /// <c>EnsureCircleExistsAsync</c> only creates a missing circle, never updates one.  This adds the
    /// grant where it is missing -- added, not reset to the built-in definition, so an owner's own edits
    /// to the circle survive -- and re-mints every member's grant through
    /// <see cref="CircleNetworkService.UpdateCircleDefinitionAsync"/>, which is what puts the storage key
    /// in.  A circle the identity does not have is skipped.
    /// </para>
    /// </remarks>
    public class V19ToV20VersionMigrationService(
        ILogger<V19ToV20VersionMigrationService> logger,
        CircleNetworkService circleNetworkService,
        CircleDefinitionService circleDefinitionService)
    {
        private static readonly IReadOnlyList<(CircleDefinition Circle, TargetDrive Drive)> ReadGrants =
        [
            (BuiltinCircles.FamilyCircle, WellKnownAppDrives.ProfileDrive),
            (BuiltinCircles.FriendsCircle, WellKnownAppDrives.ProfileDrive),
            (BuiltinCircles.WorkCircle, WellKnownAppDrives.ProfileDrive),
            (BuiltinCircles.FeedCircle, WellKnownAppDrives.PublicPostsChannelDrive)
        ];

        public async Task GrantReadToBuiltinCirclesAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            foreach (var (circle, drive) in ReadGrants)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var def = await circleDefinitionService.GetCircleAsync(circle.Id);
                if (def == null)
                {
                    logger.LogDebug("Circle {circle} not found; skipping its {drive} Read grant", circle.Name, drive.Alias);
                    continue;
                }

                if (GrantsRead(def, drive))
                {
                    continue;
                }

                logger.LogInformation("Granting {drive} Read to circle {circle}", drive.Alias, circle.Name);

                var grants = def.DriveGrants?.ToList() ?? [];
                grants.Add(new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = drive,
                        Permission = DrivePermission.Read
                    }
                });
                def.DriveGrants = grants;

                // Persists the definition and re-mints the circle grant, storage key included, for every member.
                await circleNetworkService.UpdateCircleDefinitionAsync(def, odinContext);
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            foreach (var (circle, drive) in ReadGrants)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var def = await circleDefinitionService.GetCircleAsync(circle.Id);
                if (def == null)
                {
                    continue;
                }

                if (!GrantsRead(def, drive))
                {
                    throw new OdinSystemException($"Circle {circle.Name} does not grant Read on {drive.Alias}");
                }
            }

            // Every member holding one of these circles must hold the key with it; a Read grant without
            // the storage key cannot decrypt anything.
            var connected = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);
            foreach (var identity in connected.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var (circle, drive) in ReadGrants)
                {
                    if (!identity.PeerKeyStore.CircleGrants.TryGetValue(circle.Id, out var circleGrant))
                    {
                        continue;
                    }

                    var driveGrant = circleGrant.KeyStoreKeyEncryptedDriveGrants
                        .FirstOrDefault(g => g.PermissionedDrive.Drive == drive &&
                                             g.PermissionedDrive.Permission.HasFlag(DrivePermission.Read));

                    if (driveGrant?.KeyStoreKeyEncryptedStorageKey == null)
                    {
                        throw new OdinSystemException(
                            $"{identity.OdinId} is in circle {circle.Name} without a keyed Read grant on {drive.Alias}");
                    }
                }
            }
        }

        private static bool GrantsRead(CircleDefinition def, TargetDrive drive)
        {
            return def.DriveGrants?.Any(g =>
                g.PermissionedDrive.Drive == drive &&
                g.PermissionedDrive.Permission.HasFlag(DrivePermission.Read)) ?? false;
        }
    }
}
