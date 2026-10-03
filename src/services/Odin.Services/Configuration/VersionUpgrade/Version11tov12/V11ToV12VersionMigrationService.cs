using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version11tov12
{
    /// <summary>
    /// v11 → v12: grants the Chat app (<see cref="SystemAppConstants.ChatAppId"/>) the
    /// <see cref="PermissionKeys.ReadCircleMembership"/> permission key, and doubles as the version
    /// bump that makes installs already sitting at v11 re-run <see cref="VersionUpgradeService"/>.
    ///
    /// <para>
    /// This used to grant <see cref="PermissionKeys.ManageCircleMembership"/> (51), which gated
    /// adding to circles through <c>GrantCircleAsync</c>. Both are retired (#1809): an app adds to the
    /// circles it owns with no key. What Chat still needs from 51 is what it implied -- reading circle
    /// members -- so that is what is backfilled now, as a fresh install's default grant also carries it.
    /// An app already holding 51 keeps reading members through 51's legacy implication. Every other
    /// drive grant and permission key is preserved verbatim.
    /// </para>
    ///
    /// <para>
    /// The <see cref="VersionUpgradeScheduler.RequiresUpgradeAsync"/> gate only re-enters
    /// <see cref="VersionUpgradeService.UpgradeAsync"/> when <c>currentVersion &lt; Version.DataVersionNumber</c>,
    /// so bumping <see cref="Version.DataVersionNumber"/> to 12 is also what makes the unconditional
    /// pre-pass in <see cref="VersionUpgradeService"/> — which backfills
    /// <see cref="Odin.Services.Membership.Connections.PeerKeyStore.WriteOnlyKeyPair"/> for existing
    /// connections — actually run for installs already at v11. <see cref="ValidateUpgradeAsync"/>
    /// confirms both: the Chat app permission and that pre-pass's work.
    /// </para>
    /// </summary>
    public class V11ToV12VersionMigrationService(
        ILogger<V11ToV12VersionMigrationService> logger,
        AppRegistrationService appRegistrationService,
        CircleNetworkService circleNetworkService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var apps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            foreach (var app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (app.AppId != SystemAppConstants.ChatAppId)
                {
                    continue;
                }

                // UpdateAppPermissionsAsync rebuilds the exchange grant, which would reset IsRevoked
                // to false — never touch a revoked app.
                if (app.IsRevoked)
                {
                    logger.LogDebug("Chat app {appName} is revoked; skipping ReadCircleMembership backfill", app.Name);
                    continue;
                }

                if (CanReadCircleMembers(app))
                {
                    continue;
                }

                logger.LogInformation(
                    "Granting ReadCircleMembership to the Chat app {appName} ({appId})", app.Name, app.AppId);

                // Preserve the app's existing drive grants verbatim — this migration only touches
                // the permission set.
                var drives = app.Grant.DriveGrants
                    .Select(g => new DriveGrantRequest { PermissionedDrive = g.PermissionedDrive })
                    .ToList();

                // Preserve the app's existing permission keys verbatim — only add the missing key.
                var permissionKeys = new List<int>(app.Grant.PermissionSet?.Keys ?? new List<int>())
                {
                    PermissionKeys.ReadCircleMembership
                };

                await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
                {
                    AppId = app.AppId,
                    PermissionSet = new PermissionSet(permissionKeys),
                    Drives = drives
                }, odinContext);
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var apps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            var chatApp = apps.SingleOrDefault(a => a.AppId == SystemAppConstants.ChatAppId && !a.IsRevoked);

            // A revoked or never-installed Chat app has nothing to validate.
            if (chatApp != null && !CanReadCircleMembers(chatApp))
            {
                throw new OdinSystemException(
                    $"Chat app {chatApp.Name} ({chatApp.AppId}) was not granted the ReadCircleMembership permission");
            }

            var identities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            // Identities that still require the master-key store-key encryption upgrade (e.g. no
            // TempWeakKeyStoreKey to recover from) are a known, tolerated skip in the pre-pass and
            // self-heal later via the lazy reconcile path -- not a validation failure here either.
            var stillMissing = identities.Results
                .Where(i => i.PeerKeyStore.WriteOnlyKeyPair == null && !i.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade())
                .ToList();

            if (stillMissing.Count > 0)
            {
                logger.LogWarning(
                    "{count} connected identities are missing a write-only Peer Key Store keypair after the pre-pass; " +
                    "will backfill on the owner's next grant touch for each",
                    stillMissing.Count);
            }
        }

        /// <summary>Holds ReadCircleMembership, or the retired 51, which still implies it.</summary>
        private static bool CanReadCircleMembers(RedactedAppRegistration app)
        {
            var keys = app.Grant?.PermissionSet?.Keys;
            return keys != null &&
                   (keys.Contains(PermissionKeys.ReadCircleMembership) || keys.Contains(PermissionKeys.ManageCircleMembership));
        }
    }
}
