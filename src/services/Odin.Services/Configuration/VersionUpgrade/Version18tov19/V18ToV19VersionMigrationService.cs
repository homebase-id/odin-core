using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Configuration.VersionUpgrade.Version18tov19
{
    /// <summary>
    /// v18 -&gt; v19: gives every circle an owning app.
    /// </summary>
    /// <remarks>
    /// Nothing is ownerless from here on.  A circle the owner made for themselves -- one named in the
    /// console, the two system circles -- belongs to the owner console,
    /// <see cref="SystemAppConstants.OwnerConsoleAppId"/>, rather than to nobody.
    ///
    /// <para>
    /// This used to finish every drive's owner and address too.  That moved into the Drives NOT NULL
    /// migration (<c>TableDrivesMigrationV202610130807</c>, #1901), which runs at startup, so an identity
    /// whose owner has not signed in is not left with NULLs; by the time this runs every drive is done.
    /// </para>
    ///
    /// <para>
    /// Idempotent: a circle that already names an app is skipped, so a partial run is simply repeated.
    /// </para>
    /// </remarks>
    public class V18ToV19VersionMigrationService(
        ILogger<V18ToV19VersionMigrationService> logger,
        CircleDefinitionService circleDefinitionService)
    {
        /// <summary>
        /// The circle pass; the same work the ladder runs, for a caller that wants one call.
        /// </summary>
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            await StampOwnerConsoleCirclesAsync(odinContext, cancellationToken);
        }

        /// <summary>
        /// Gives every ownerless circle to the owner console.
        /// </summary>
        /// <remarks>
        /// The two system circles included.  They belong to no app today, which reads as the owner's own
        /// and is exactly right -- the owner console is where they are administered -- so naming that
        /// owner costs them nothing and takes the last nulls out of the column.  An app is still refused
        /// them, by the same checks as before: what changes is that those checks now ask whose the circle
        /// is rather than whether anyone's.
        /// </remarks>
        public async Task<int> StampOwnerConsoleCirclesAsync(IOdinContext odinContext,
            CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            var circles = await circleDefinitionService.GetCirclesAsync(includeSystemCircle: true);

            var stamped = 0;
            foreach (var circle in circles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skipped off the copy in hand, so an app-owned circle costs no round trip. The stamp
                // re-checks the stored row before writing regardless.
                if (circle.AppId.HasValue)
                {
                    continue;
                }

                if (await circleDefinitionService.StampOwnerConsoleIfUnsetAsync(circle.Id))
                {
                    stamped++;
                    logger.LogDebug("v18->v19: circle {name} is now the owner console's", circle.Name);
                }
            }

            logger.LogInformation("v18->v19: gave {count} circle(s) to the owner console", stamped);
            return stamped;
        }

        /// <summary>
        /// Fails the upgrade if any circle is still ownerless.
        /// </summary>
        /// <remarks>
        /// Fails rather than logs, unlike the validation on some earlier steps.  Those re-check a backfill
        /// that only ever adds, so a miss leaves a contact with what they already had; this one is the
        /// premise the code above it now relies on -- every circle has an owner -- and a row
        /// that slipped through would be one no uniqueness constraint covers and no "is this the owner's?"
        /// check reads correctly.
        /// </remarks>
        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            cancellationToken.ThrowIfCancellationRequested();

            var circles = await circleDefinitionService.GetCirclesAsync(includeSystemCircle: true);

            var ownerless = circles
                .Where(c => c.AppId == null)
                .Select(c => $"{c.Name} ({c.Id})")
                .ToList();

            if (ownerless.Count > 0)
            {
                throw new OdinSystemException(
                    $"v18->v19: {ownerless.Count} circle(s) still have no owning app: " +
                    string.Join(", ", ownerless));
            }

            logger.LogInformation("v18->v19: every circle has an owning app");
        }
    }
}
