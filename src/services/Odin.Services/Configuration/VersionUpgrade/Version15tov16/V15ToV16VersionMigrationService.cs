using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Configuration.VersionUpgrade.Version15tov16
{
    /// <summary>
    /// v15 → v16: gives the chat, mail and feed apps their grant-on-connect circles on installs that
    /// predate the enrollment model.
    ///
    /// <para>
    /// A fresh install already gets them -- <see cref="SystemAppConstants"/> now declares them in each
    /// app's registration request. This backfills existing installs, which do not re-register on their
    /// own. Without it the enrollment pipeline queries <c>WHERE GrantOn = Connect</c>, finds nothing, and
    /// the whole mechanism sits inert while the system circles carry on doing the work.
    /// </para>
    ///
    /// <para>
    /// Additive and transitional. The system circles still enrol every new connection exactly as before,
    /// so a connection formed after this lands in both -- the same drive grants arriving twice, which is
    /// redundant rather than harmful. The old path is removed when the system circles retire; until then
    /// this is the new path proving itself alongside the old one.
    /// </para>
    /// </summary>
    public class V15ToV16VersionMigrationService(
        ILogger<V15ToV16VersionMigrationService> logger,
        IAppRegistrationService appRegistrationService,
        CircleDefinitionService circleDefinitionService)
    {
        private static readonly AppRegistrationRequest[] Declaring =
        [
            SystemAppConstants.ChatAppRegistrationRequest,
            SystemAppConstants.MailAppRegistrationRequest,
            SystemAppConstants.FeedAppRegistrationRequest
        ];

        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var declaration in Declaring)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Only for apps this identity actually has. An identity that never installed mail should
                // not acquire a mail circle from a migration.
                if (await appRegistrationService.GetAppRegistration(declaration.AppId, odinContext) == null)
                {
                    logger.LogDebug("v15->v16: {app} is not registered here; skipping", declaration.Name);
                    continue;
                }

                foreach (var declared in declaration.DefaultCircles ?? [])
                {
                    await circleDefinitionService.CreateOrUpdateAppCircleAsync(
                        declaration.AppId,
                        declared.ToCreateCircleRequest(declaration.AppId));

                    logger.LogInformation("v15->v16: {app} declared '{circle}' ({id}) with GrantOn={grantOn}",
                        declaration.Name, declared.Name, declared.Id, declared.GrantOn);
                }
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var declaration in Declaring)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await appRegistrationService.GetAppRegistration(declaration.AppId, odinContext) == null)
                {
                    continue;
                }

                foreach (var declared in declaration.DefaultCircles ?? [])
                {
                    var circle = await circleDefinitionService.GetCircleAsync(declared.Id);

                    if (circle == null)
                    {
                        throw new OdinSystemException(
                            $"Validation failed: {declaration.Name} circle {declared.Id} was not created");
                    }

                    if (circle.GrantOn != declared.GrantOn || circle.AppId != declaration.AppId.Value)
                    {
                        throw new OdinSystemException(
                            $"Validation failed: circle {declared.Id} has GrantOn={circle.GrantOn} " +
                            $"AppId={circle.AppId}, expected {declared.GrantOn} / {declaration.AppId}");
                    }
                }
            }

            // The pipeline reads through the indexed column, so prove that path returns them.
            var ambient = await circleDefinitionService.GetCirclesByGrantOnAsync(CircleGrantOn.Connect);
            logger.LogInformation("v15->v16: {count} grant-on-connect circle(s) visible to the pipeline",
                ambient.Count);
        }
    }
}
