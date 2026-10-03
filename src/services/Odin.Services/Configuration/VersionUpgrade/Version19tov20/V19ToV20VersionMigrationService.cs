using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version19tov20
{
    /// <summary>
    /// v19 → v20: retires the system connection circles, Confirmed Connections and Auto-connected (#1809).
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
    /// <para>
    /// A deposit an app left into one of these circles was sealed without the new drive's key.  The upgrade's
    /// deposit pre-pass converts the ones it can reach before this runs; the rest (connections it had to
    /// skip) are turned back into pending enrollments so the owning app redoes them with the key.
    /// </para>
    /// <para>
    /// Last, the two system circles themselves are deleted: out of every app's authorized circles, out of
    /// every connection record, their membership rows, then the definitions.  Their ids live only here now.
    /// </para>
    /// </remarks>
    public class V19ToV20VersionMigrationService(
        ILogger<V19ToV20VersionMigrationService> logger,
        CircleNetworkService circleNetworkService,
        CircleDefinitionService circleDefinitionService,
        CircleMembershipService circleMembershipService,
        IAppRegistrationService appRegistrationService)
    {
        /// <summary>
        /// The retired Confirmed Connections and Auto-connected circles.  Never reuse these ids.
        /// </summary>
        public static readonly IReadOnlyList<Guid> RetiredSystemCircleIds =
        [
            Guid.Parse("bb2683fa402aff866e771a6495765a15"),
            Guid.Parse("9e22b42952f74d2580e11250b651d343")
        ];

        private static readonly IReadOnlyList<(CircleDefinition Circle, TargetDrive Drive)> ReadGrants =
        [
            (BuiltinCircles.FamilyCircle, WellKnownAppDrives.ProfileDrive),
            (BuiltinCircles.FriendsCircle, WellKnownAppDrives.ProfileDrive),
            (BuiltinCircles.WorkCircle, WellKnownAppDrives.ProfileDrive),
            (BuiltinCircles.FeedCircle, WellKnownAppDrives.PublicPostsChannelDrive)
        ];

        /// <summary>
        /// Turns any deposit still pending for one of these circles back into a pending enrollment.  Returns
        /// the connections affected, so the owning apps can be told once the transaction has committed.
        /// </summary>
        public async Task<List<OdinId>> RequeueStaleDepositsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            return await circleNetworkService.RequeueDepositsAsPendingEnrollmentsAsync(
                ReadGrants.Select(g => g.Circle.Id.Value).Distinct().ToList(), odinContext, cancellationToken);
        }

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

        public async Task DeleteSystemCirclesAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            // Apps first.  Chat and Mail named both circles; with them gone from the list, nothing re-issues
            // the app grants the connection pass below removes.
            foreach (var app in await appRegistrationService.GetRegisteredAppsAsync(odinContext))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!(app.AuthorizedCircles?.Any(RetiredSystemCircleIds.Contains) ?? false))
                {
                    continue;
                }

                logger.LogInformation("Removing the system circles from app {app}'s authorized circles", app.Name);
                await appRegistrationService.UpdateAuthorizedCirclesAsync(new UpdateAuthorizedCirclesRequest
                {
                    AppId = app.AppId,
                    AuthorizedCircles = app.AuthorizedCircles.Where(c => !RetiredSystemCircleIds.Contains(c)).ToList(),
                    CircleMemberPermissionGrant = app.CircleMemberPermissionSetGrantRequest
                }, odinContext);
            }

            var changed = await circleNetworkService.RemoveCirclesFromAllConnectionsAsync(RetiredSystemCircleIds,
                odinContext, cancellationToken);
            logger.LogInformation("Removed the system circles from {count} connection(s)", changed);

            foreach (var circleId in RetiredSystemCircleIds)
            {
                // Rows a connection record no longer names, and any held by something other than a connection.
                await circleMembershipService.DeleteAllMembersOfCircleAsync(circleId);

                if (await circleDefinitionService.GetCircleAsync(circleId) != null)
                {
                    await circleDefinitionService.DeleteAsync(circleId);
                }
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            foreach (var circleId in RetiredSystemCircleIds)
            {
                if (await circleDefinitionService.GetCircleAsync(circleId) != null)
                {
                    throw new OdinSystemException($"System circle {circleId} still exists");
                }
            }

            if ((await appRegistrationService.GetRegisteredAppsAsync(odinContext))
                .Any(a => a.AuthorizedCircles?.Any(RetiredSystemCircleIds.Contains) ?? false))
            {
                throw new OdinSystemException("An app still authorizes a retired system circle");
            }

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

                var store = identity.PeerKeyStore;
                if (RetiredSystemCircleIds.Any(id => store.CircleGrants.ContainsKey(id) ||
                                                     store.AppGrants.Values.Any(a => a.ContainsKey(id))))
                {
                    throw new OdinSystemException($"{identity.OdinId} still holds a retired system circle");
                }

                foreach (var (circle, drive) in ReadGrants)
                {
                    if (identity.PeerKeyStore.DepositedGrants?.Any(d => d.CircleId == circle.Id) ?? false)
                    {
                        throw new OdinSystemException(
                            $"{identity.OdinId} still has a deposit for circle {circle.Name}, which would convert without the {drive.Alias} key");
                    }

                    if (!identity.PeerKeyStore.CircleGrants.TryGetValue(circle.Id, out var circleGrant))
                    {
                        continue;
                    }

                    if (!circleGrant.KeyStoreKeyEncryptedDriveGrants.Any(g => g.PermissionedDrive.Drive == drive && g.IsKeyedRead))
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
