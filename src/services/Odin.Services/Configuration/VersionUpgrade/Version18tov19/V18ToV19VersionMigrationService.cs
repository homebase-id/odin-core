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
    /// Why it matters is on <see cref="SystemAppConstants.OwnerConsoleAppId"/>: an ownerless row's slug
    /// sits outside <c>UNIQUE(identityId, AppId, DriveSlug)</c> entirely.  Stamping a real owner is what
    /// puts every slug inside the constraint.  It also removes an ambiguity the code had to keep
    /// re-deciding -- a null <c>AppId</c> was the test for "the owner's own" in half a dozen places, and
    /// each had to be read carefully to see whether the absence meant the owner or meant unknown.
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

            await FinishDriveAddressesAsync(odinContext, cancellationToken);
            await StampOwnerConsoleCirclesAsync(odinContext, cancellationToken);
        }

        /// <summary>
        /// Finishes every drive's address: an owner for the ones that have none, and the missing half of
        /// an address for any drive carrying only part of one.
        /// </summary>
        /// <remarks>
        /// Two populations, one pass.  The ownerless drives are the ones no app declares -- created
        /// through the owner console or by the setup wizard, neither of which named an app; v13 -&gt; v14
        /// gave most of them a slug and deliberately left the owner alone, which is the state this
        /// finishes.  App-owned drives are here too, because the old create path settled for whatever
        /// <c>TypeSlugFor</c> returned and that was null for any type it did not recognise -- so a
        /// third-party app's drive carries an owner and a slug and no type slug.  Validation below covers
        /// every drive, so leaving those behind would fail the upgrade on rows nothing else would fix.
        /// <para>
        /// An owner is filled, never moved: a drive that already names an app keeps it, and only the
        /// missing parts of its address are written.
        /// </para>
        /// <para>
        /// Uniqueness is per owning app, so the slugs to avoid are the ones that app already holds --
        /// tracked per app, not identity-wide, since feed/news and chat/news may coexist.  Each set grows
        /// as the pass runs, because two drives finished in the same run compete with each other and the
        /// database is not consulted between rows.
        /// </para>
        /// </remarks>
        public async Task<int> FinishDriveAddressesAsync(IOdinContext odinContext,
            CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            var everyDrive = await driveManager.GetDrivesAsync(PageOptions.All, odinContext);

            var takenByApp = everyDrive.Results
                .Where(d => d.AppId != null && !string.IsNullOrWhiteSpace(d.DriveSlug))
                .GroupBy(d => d.AppId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => new HashSet<string>(g.Select(d => d.DriveSlug), StringComparer.Ordinal));

            var finished = 0;
            foreach (var drive in everyDrive.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var carried = string.IsNullOrWhiteSpace(drive.DriveSlug) ? null : drive.DriveSlug;
                var carriedTypeSlug = string.IsNullOrWhiteSpace(drive.DriveTypeSlug) ? null : drive.DriveTypeSlug;

                if (drive.AppId != null && carried != null && carriedTypeSlug != null)
                {
                    continue;
                }

                var appId = drive.AppId ?? SystemAppConstants.OwnerConsoleAppId;
                if (!takenByApp.TryGetValue(appId, out var taken))
                {
                    taken = new HashSet<string>(StringComparer.Ordinal);
                    takenByApp[appId] = taken;
                }

                // A slug carried over from the ownerless era was unconstrained, so it may be one the
                // owner console already holds. Dropped in that case and re-derived below: the address
                // resolved to nothing before this (the wire address needs the app's half too), so
                // nothing anyone can reach is being moved. A drive that already had an owner cannot be
                // in this position -- its slug was inside the constraint all along.
                if (drive.AppId == null && carried != null && taken.Contains(carried))
                {
                    logger.LogInformation(
                        "v18->v19: drive {name} carried the slug {slug}, which the owner console already " +
                        "holds; deriving another", drive.Name, carried);
                    carried = null;
                }

                // Generate never returns a slug already in the set, so one call is enough.
                var slug = carried ?? DriveSlugGenerator.Generate(drive.Id, drive.Name, taken);
                taken.Add(slug);

                var typeSlug = carriedTypeSlug ??
                               DriveSlugGenerator.TypeSlugOrDefault(drive.Id, drive.TargetDriveInfo.Type);

                await driveManager.ApplyAddressAsync(drive.Id, appId, slug, typeSlug, odinContext);

                finished++;
                logger.LogDebug("v18->v19: drive {name} is now {appId} {typeSlug}/{slug}",
                    drive.Name, appId, typeSlug, slug);
            }

            logger.LogInformation("v18->v19: finished the address of {count} drive(s)", finished);
            return finished;
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

                // The "is it already owned?" test lives in StampOwningAppIfUnsetAsync, which answers it
                // against the stored row; asking it again here from the loop's copy would be a second
                // place to keep in step.
                if (await circleDefinitionService.StampOwningAppIfUnsetAsync(circle.Id,
                        SystemAppConstants.OwnerConsoleAppId))
                {
                    stamped++;
                    logger.LogDebug("v18->v19: circle {name} is now the owner console's", circle.Name);
                }
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
