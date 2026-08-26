using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Configuration.VersionUpgrade.Version13tov14
{
    /// <summary>
    /// v13 -> v14: hands the identity's relationship circles -- Friends, Family, Work, Acquaintances --
    /// to the chat app, so the app that presents them also owns them.
    ///
    /// <para>
    /// These used to exist only because the owner console's setup wizard created them, through
    /// <see cref="InitialSetupRequest.Circles"/>, which means they arrived as owner circles
    /// (<c>AppId == null</c>) on every identity that ran it. A new identity now gets them chat-owned from
    /// <c>TenantConfigService.RegisterChatAppAsync</c>; this is the same set on the identities that
    /// predate that, and the two agree because both use <see cref="SystemAppConstants.ChatRelationshipCircles"/>.
    /// </para>
    ///
    /// <para>
    /// Nothing else can move them: <see cref="CircleDefinitionService.UpdateAsync"/> refuses to reassign
    /// <c>AppId</c> at all, and <see cref="CircleDefinitionService.CreateOrUpdateAppCircleAsync"/> refuses
    /// to take over a circle the app does not already own. Hence a migration, using
    /// <see cref="CircleDefinitionService.SetOwningAppAsync"/>.
    /// </para>
    ///
    /// <para>
    /// Matched by id, which is <c>md5(name)</c> on both sides, so the circles are found whatever the owner
    /// has since renamed them to. An identity that never ran the wizard simply has nothing to move: the
    /// migration rebinds what is there and never creates a circle that was not.
    /// </para>
    ///
    /// <para>
    /// Ownership only. Membership, grants, name, description and <see cref="CircleGrantOn"/> are left
    /// untouched -- these are the owner's circles with the owner's people in them, and the migration is
    /// about which app manages them from here, not about what they grant. It runs whether or not chat is
    /// installed: an identity that installs chat later would otherwise hit
    /// <see cref="OdinClientErrorCode.CircleNotOwnedByApp"/> the first time the app tried to manage one.
    /// </para>
    /// </summary>
    public class V13ToV14VersionMigrationService(
        ILogger<V13ToV14VersionMigrationService> logger,
        CircleDefinitionService circleDefinitionService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var circle = await circleDefinitionService.GetCircleAsync(declared.Id);

                if (circle == null)
                {
                    logger.LogDebug("v13->v14: no '{circle}' circle ({id}) on this identity; skipping",
                        declared.Name, declared.Id);
                    continue;
                }

                if (circle.AppId.HasValue && circle.AppId != SystemAppConstants.ChatAppId)
                {
                    // Some other app already owns it. Taking it away would break that app's ability to
                    // manage its own circle, which is not this migration's call to make.
                    logger.LogWarning("v13->v14: '{circle}' ({id}) is owned by app {appId}; leaving it alone",
                        circle.Name, declared.Id, circle.AppId);
                    continue;
                }

                await circleDefinitionService.SetOwningAppAsync(declared.Id, SystemAppConstants.ChatAppId);

                logger.LogInformation("v13->v14: '{circle}' ({id}) is now owned by the chat app",
                    circle.Name, declared.Id);
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var circle = await circleDefinitionService.GetCircleAsync(declared.Id);

                if (circle == null)
                {
                    continue;
                }

                // A circle another app already owned was deliberately skipped by the upgrade; still an
                // owner circle is the one outcome that means the upgrade did not do its job.
                if (circle.AppId == null)
                {
                    throw new OdinSystemException(
                        $"Validation failed: circle '{declared.Name}' ({declared.Id}) is still an owner circle");
                }
            }
        }
    }
}
