using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Apps.Builtin;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Configuration.VersionUpgrade.Version16tov17
{
    /// <summary>
    /// v16 -&gt; v17: gives the built-in circles the emoji the tree names.
    /// </summary>
    /// <remarks>
    /// The emoji is declared on <see cref="BuiltinCircles"/>, and provisioning carries it onto the row --
    /// but provisioning only ever creates, so every circle that already existed when the emoji was declared
    /// still has none.  That is every identity on v16, which is why this exists rather than a change to
    /// <c>BuiltinProvisioner</c>.
    ///
    /// <para>
    /// Fill-only, via <see cref="CircleDefinitionService.ApplyTreeEmojiIfUnsetAsync"/>: the emoji is
    /// editable by the owner, so a circle that already carries one is left alone.  That also makes this
    /// safe to re-run, and means the validation below asserts only that an emoji is present -- not that it
    /// is the tree's.
    /// </para>
    /// </remarks>
    public class V16ToV17VersionMigrationService(
        ILogger<V16ToV17VersionMigrationService> logger,
        CircleDefinitionService circleDefinitionService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            var filled = 0;
            foreach (var def in BuiltinApps.AllCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await circleDefinitionService.ApplyTreeEmojiIfUnsetAsync(def.Id, def.Emoji))
                {
                    filled++;
                    logger.LogDebug("v16->v17: circle {circle} now shows {emoji}", def.Name, def.Emoji);
                }
            }

            logger.LogInformation("v16->v17: filled the emoji on {count} circle(s)", filled);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            foreach (var def in BuiltinApps.AllCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(def.Emoji))
                {
                    continue;
                }

                // A circle the tree declares but this identity never provisioned has nothing to fill.
                var circle = await circleDefinitionService.GetCircleAsync(def.Id);
                if (circle == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(circle.Emoji))
                {
                    throw new OdinSystemException(
                        $"v16->v17: circle {def.Name} still has no emoji");
                }
            }
        }
    }
}
