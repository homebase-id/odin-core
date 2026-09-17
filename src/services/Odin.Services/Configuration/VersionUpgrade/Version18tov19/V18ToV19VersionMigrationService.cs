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
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Configuration.VersionUpgrade.Version18tov19
{
    /// <summary>
    /// v18 -&gt; v19: gives every drive and every circle an owning app, and every drive an address.
    /// </summary>
    /// <remarks>
    /// Nothing is ownerless from here on.  A drive or circle the owner made for themselves -- the wallet
    /// drive, an ad-hoc drive, a circle named in the console, the two system circles -- belongs to the
    /// owner console, <see cref="SystemAppConstants.OwnerConsoleAppId"/>, rather than to nobody.
    ///
    /// <para>
    /// The point is <c>UNIQUE(identityId, AppId, DriveSlug)</c>.  NULLs do not collide in a unique index
    /// in either dialect, so the slug of an ownerless drive is constrained by nothing: two drives could
    /// hold the same one and the database would not object.  Stamping a real owner is what puts every
    /// slug inside the constraint.  It also removes an ambiguity the code had to keep re-deciding -- a
    /// null <c>AppId</c> was the test for "the owner's own" in half a dozen places, and each had to be
    /// read carefully to see whether the absence meant the owner or meant unknown.
    /// </para>
    ///
    /// <para>
    /// Two passes, drives then circles, each idempotent: a row that already names an app is skipped, so a
    /// partial run is simply repeated.  Drives first because the circle pass logs against them and
    /// because a drive's address is the longer job.
    /// </para>
    ///
    /// <para>
    /// Slugs are filled, never corrected.  A drive carrying a slug carries an address other identities
    /// may already resolve against, and re-deriving it on the way past would move that address for a
    /// reason that has nothing to do with ownership.  The type slug is filled the same way, falling back
    /// to <see cref="DriveSlugGenerator.DefaultTypeSlug"/> for a drive of no recognised type, since a
    /// drive with an owner and no type slug would be half-addressed.
    /// </para>
    /// </remarks>
    public class V18ToV19VersionMigrationService(
        ILogger<V18ToV19VersionMigrationService> logger,
        DriveManager driveManager,
        CircleDefinitionService circleDefinitionService)
    {
        /// <summary>
        /// Both passes, in order.  The ladder runs them as separate phases so a failure names the step it
        /// happened in; this is the same work for a caller that wants one call.
        /// </summary>
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            await StampOwnerConsoleDrivesAsync(odinContext, cancellationToken);
            await StampOwnerConsoleCirclesAsync(odinContext, cancellationToken);
        }

        /// <summary>
        /// Gives every ownerless drive to the owner console, with a slug and a type slug.
        /// </summary>
        /// <remarks>
        /// The drives that reach here are the ones no app declares: created through the owner console or
        /// by the setup wizard, neither of which named an app.  v13 -&gt; v14 gave most of them a slug and
        /// deliberately left the owner alone, which is the state this finishes.
        /// <para>
        /// Uniqueness is per owning app, so the set to avoid is every slug the owner console already
        /// holds -- including the drives that ship with an identity, since the system app and the owner
        /// console are the same id.  It grows as the pass runs, because two drives stamped in the same
        /// run compete with each other and the database is not consulted between rows.
        /// </para>
        /// </remarks>
        public async Task<int> StampOwnerConsoleDrivesAsync(IOdinContext odinContext,
            CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            var everyDrive = await driveManager.GetDrivesAsync(PageOptions.All, odinContext);

            var taken = new HashSet<string>(
                everyDrive.Results
                    .Where(d => SystemAppConstants.IsOwnerConsole(d.AppId) && d.AppId != null &&
                                !string.IsNullOrWhiteSpace(d.DriveSlug))
                    .Select(d => d.DriveSlug),
                StringComparer.Ordinal);

            var stamped = 0;
            foreach (var drive in everyDrive.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (drive.AppId != null)
                {
                    continue;
                }

                var slug = string.IsNullOrWhiteSpace(drive.DriveSlug)
                    ? DriveSlugGenerator.Generate(drive.Id, drive.Name, taken)
                    : drive.DriveSlug;

                // A slug carried over from the ownerless era was unconstrained, so it may be one the
                // owner console already holds. Re-derived in that case: the address resolved to nothing
                // before this (the wire address needs the app's half too), so nothing is being moved.
                if (taken.Contains(slug))
                {
                    logger.LogInformation(
                        "v18->v19: drive {name} carried the slug {slug}, which the owner console already " +
                        "holds; deriving another", drive.Name, slug);
                    slug = DriveSlugGenerator.Generate(drive.Id, drive.Name, taken);
                }

                taken.Add(slug);

                var typeSlug = string.IsNullOrWhiteSpace(drive.DriveTypeSlug)
                    ? DriveSlugGenerator.TypeSlugOrDefault(drive.Id, drive.TargetDriveInfo.Type)
                    : drive.DriveTypeSlug;

                await driveManager.ApplyAddressAsync(drive.Id, SystemAppConstants.OwnerConsoleAppId, slug,
                    typeSlug, odinContext);

                stamped++;
                logger.LogDebug("v18->v19: drive {name} is now owner-console {typeSlug}/{slug}",
                    drive.Name, typeSlug, slug);
            }

            logger.LogInformation("v18->v19: gave {count} drive(s) to the owner console", stamped);
            return stamped;
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

                if (circle.AppId != null)
                {
                    continue;
                }

                await circleDefinitionService.StampOwningAppIfUnsetAsync(circle.Id,
                    SystemAppConstants.OwnerConsoleAppId);

                stamped++;
                logger.LogDebug("v18->v19: circle {name} is now the owner console's", circle.Name);
            }

            logger.LogInformation("v18->v19: gave {count} circle(s) to the owner console", stamped);
            return stamped;
        }

        /// <summary>
        /// Fails the upgrade if anything is still ownerless or unaddressed.
        /// </summary>
        /// <remarks>
        /// Fails rather than logs, unlike the validation on some earlier steps.  Those re-check a backfill
        /// that only ever adds, so a miss leaves a contact with what they already had; this one is the
        /// premise the code above it now relies on -- every drive and circle has an owner -- and a row
        /// that slipped through would be one no uniqueness constraint covers and no "is this the owner's?"
        /// check reads correctly.
        /// </remarks>
        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            cancellationToken.ThrowIfCancellationRequested();

            var everyDrive = await driveManager.GetDrivesAsync(PageOptions.All, odinContext);

            var unaddressed = everyDrive.Results
                .Where(d => d.AppId == null || string.IsNullOrWhiteSpace(d.DriveSlug) ||
                            string.IsNullOrWhiteSpace(d.DriveTypeSlug))
                .Select(d => $"{d.Name} ({d.Id})")
                .ToList();

            if (unaddressed.Count > 0)
            {
                throw new OdinSystemException(
                    $"v18->v19: {unaddressed.Count} drive(s) still have no owning app or no address: " +
                    string.Join(", ", unaddressed));
            }

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

            logger.LogInformation("v18->v19: every drive and circle has an owning app");
        }
    }
}
