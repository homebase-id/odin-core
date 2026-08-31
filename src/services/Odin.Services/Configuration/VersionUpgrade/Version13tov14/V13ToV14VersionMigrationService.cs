using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Apps;
using Odin.Services.Drives;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Configuration.VersionUpgrade.Version13tov14
{
    /// <summary>
    /// v13 -&gt; v14: brings an existing identity onto the app tree -- every drive and circle gets the
    /// owning app, slug and enrolment it would have been created with today, and the apps that are now
    /// built-in but were not before are registered.
    /// </summary>
    /// <remarks>
    /// The columns landed earlier and nothing ever filled them, so on a tenant that predates this every
    /// drive carries <c>AppId</c>, <c>DriveSlug</c> and <c>DriveTypeSlug</c> as NULL, and the circles the
    /// setup wizard created carry no <c>AppId</c>.  <see cref="BuiltinApps"/> says what those values are.
    ///
    /// <para>
    /// Two halves, and they are different jobs.  <b>Stamping</b> fills in rows that already exist and is
    /// the only part that needs migration-only setters, because ownership is deliberately not
    /// reassignable through the normal write paths.  <b>Provisioning</b> then creates what is missing,
    /// and is exactly what a new identity gets -- <see cref="BuiltinProvisioner"/>, unchanged and
    /// idempotent.  Stamping runs first so provisioning sees a drive that is already owned rather than
    /// trying to create one that is there.
    /// </para>
    ///
    /// <para>
    /// Additive throughout.  A drive or circle that already has an owner is left alone rather than
    /// reassigned, so a partial run can be repeated and a value someone set by hand is never overwritten.
    /// Nothing is deleted: <c>WalletDrive</c> stops being seeded for new identities, but the ones that
    /// already have it keep it, stamped like the rest.
    /// </para>
    /// </remarks>
    public class V13ToV14VersionMigrationService(
        ILogger<V13ToV14VersionMigrationService> logger,
        DriveManager driveManager,
        IAppRegistrationService appRegistrationService,
        CircleDefinitionService circleDefinitionService,
        BuiltinProvisioner builtinProvisioner)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            await StampDrivesAsync(odinContext, cancellationToken);
            await StampChannelDrivesAsync(odinContext, cancellationToken);
            await StampRemainingDrivesAsync(odinContext, cancellationToken);
            await StampCirclesAsync(cancellationToken);
            await StampAppSlugsAsync(odinContext, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Creates whatever is still missing: the drives and circles of apps that are built-in now but
            // were not before, and their registrations.
            await builtinProvisioner.EnsureAllAsync(odinContext);
        }

        /// <summary>
        /// Gives every drive the tree names the ownership and address it would have been created with.
        /// </summary>
        /// <remarks>
        /// Walks the whole tree, not just the built-in apps: ListsDrive, MomentsDrive and WalletDrive
        /// belong to apps that are not built-in, yet exist on every identity because they were seeded
        /// before ownership existed.  Those three are the reason this cannot use
        /// <see cref="BuiltinApps.Builtin"/> alone.
        /// </remarks>
        private async Task StampDrivesAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            foreach (var request in BuiltinApps.AllDrives)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var drive = await driveManager.GetDriveAsync(request.TargetDrive.Alias);
                if (drive == null)
                {
                    // Not on this identity -- either its app was never installed, or the drive is one the
                    // tree names ahead of it being built. Provisioning creates it if it should exist.
                    continue;
                }

                if (request.AppId == null)
                {
                    throw new OdinSystemException(
                        $"Drive {request.Name} is in the tree with no owning app; every drive must have one");
                }

                var changed = await driveManager.ApplyAddressAsync(drive.Id, request.AppId.Value,
                    request.DriveSlug, request.DriveTypeSlug, odinContext);

                if (changed)
                {
                    logger.LogDebug("v13->v14: {drive} is now {slug}, owned by {appId}",
                        request.Name, request.DriveSlug, request.AppId);
                }
            }
        }

        /// <summary>
        /// Gives every user-created channel drive to the feed app, with a slug derived from its name.
        /// </summary>
        /// <remarks>
        /// These are the one kind of drive the tree cannot list: a user creates channel drives at will,
        /// so there are arbitrarily many and none has a fixed alias.  What they share is a type, which is
        /// why <c>DriveSlugGenerator</c> keeps its derivation for exactly this case.
        /// <para>
        /// <b>Fills rather than corrects</b>, unlike the tree-declared drives.  The tree is authoritative
        /// for what it declares, and it does not declare these -- their slug is derived from a name the
        /// owner chose and may have since changed, so re-deriving on every upgrade would move an address
        /// that other identities resolve against.  A channel drive that already has one is left alone.
        /// </para>
        /// <para>
        /// Runs after <see cref="StampDrivesAsync"/> so the feed app's own drives already hold their
        /// slugs, and the taken set read back from storage is complete.  Uniqueness is per owning app --
        /// <c>UNIQUE(identityId, AppId, DriveSlug)</c> -- so only feed's slugs can collide.
        /// </para>
        /// </remarks>
        private async Task StampChannelDrivesAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var channels = await driveManager.GetDrivesAsync(
                SystemDriveConstants.ChannelDriveType, PageOptions.All, odinContext);

            var everyDrive = await driveManager.GetDrivesAsync(PageOptions.All, odinContext);

            var taken = new HashSet<string>(
                everyDrive.Results
                    .Where(d => d.AppId == SystemAppConstants.FeedAppId && !string.IsNullOrWhiteSpace(d.DriveSlug))
                    .Select(d => d.DriveSlug),
                StringComparer.Ordinal);

            foreach (var drive in channels.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (drive.AppId != null && !string.IsNullOrWhiteSpace(drive.DriveSlug))
                {
                    continue;
                }

                var slug = DriveSlugGenerator.Generate(drive.Id, drive.Name, taken);
                taken.Add(slug);

                // "channel" stated, not looked up: these came from a query on ChannelDriveType, so the
                // type is already known, and TypeSlugFor can return null -- which AssertValidOrNull
                // permits, so a miss would be stored silently.
                await driveManager.ApplyAddressAsync(drive.Id, SystemAppConstants.FeedAppId, slug,
                    BuiltinDrives.ChannelDriveTypeSlug, odinContext);

                logger.LogDebug("v13->v14: channel drive {name} is now feed/{slug}", drive.Name, slug);
            }
        }

        /// <summary>
        /// Gives a slug to every drive the earlier passes did not reach.
        /// </summary>
        /// <remarks>
        /// Drives nobody declares: created through the owner console, or by the setup wizard's own
        /// <c>request.Drives</c>, neither of which sets an owning app.  They are not in the tree and are
        /// not channel-typed, so nothing above touches them, and they would come out of the upgrade with
        /// no address at all.
        /// <para>
        /// <b>Slug only.</b>  The owning app is left as it was -- usually null -- and the type slug too,
        /// since an unknown drive type has no readable form and the drive name describes the drive rather
        /// than its category.  That departs from the set-together rule in
        /// <c>docs/drive-addressing.md</c>, which means <c>UNIQUE(identityId, AppId, DriveSlug)</c> does
        /// not constrain these rows: NULL app ids do not collide in either dialect.  So uniqueness among
        /// them is enforced here instead, by deduping against every other slug held with no owning app.
        /// </para>
        /// </remarks>
        private async Task StampRemainingDrivesAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var everyDrive = await driveManager.GetDrivesAsync(PageOptions.All, odinContext);

            // The database cannot police these, so the taken set has to: every slug already held by a
            // drive with no owning app is one this pass must avoid.
            var taken = new HashSet<string>(
                everyDrive.Results
                    .Where(d => d.AppId == null && !string.IsNullOrWhiteSpace(d.DriveSlug))
                    .Select(d => d.DriveSlug),
                StringComparer.Ordinal);

            foreach (var drive in everyDrive.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(drive.DriveSlug))
                {
                    continue;
                }

                var slug = DriveSlugGenerator.Generate(drive.Id, drive.Name, taken);
                taken.Add(slug);

                await driveManager.ApplyAddressAsync(drive.Id, drive.AppId, slug, drive.DriveTypeSlug,
                    odinContext);

                logger.LogDebug("v13->v14: undeclared drive {name} is now {slug}", drive.Name, slug);
            }
        }

        /// <summary>
        /// Gives every circle the tree names its owning app, enrolment and designation.
        /// </summary>
        /// <remarks>
        /// In practice this is the five that exist before the upgrade: Emergency Location Access, and
        /// Friends, Family, Work and Acquaintances, which the owner console's setup wizard created
        /// client-side.  The rest do not exist yet and are created by provisioning, already owned.  The
        /// two system circles are not in the tree and are left alone -- they belong to no app.
        /// </remarks>
        private async Task StampCirclesAsync(CancellationToken cancellationToken)
        {
            foreach (var def in BuiltinApps.AllCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (def.AppId == null)
                {
                    throw new OdinSystemException(
                        $"Circle {def.Name} is in the tree with no owning app; every circle must have one");
                }

                var changed = await circleDefinitionService.ApplyTreeDefinitionAsync(
                    def.Id, def.AppId.Value, def.GrantOn, def.Designation);

                if (changed)
                {
                    logger.LogDebug("v13->v14: circle {circle} is now owned by {appId}, GrantOn {grantOn}",
                        def.Name, def.AppId, def.GrantOn);
                }
            }
        }

        /// <summary>
        /// Gives every registered app the slug the tree names.
        /// </summary>
        /// <remarks>
        /// A registration built before the tree was authoritative derived its slug from the display
        /// name, so "Homebase - Location" was registered as <c>homebase-locat</c>.  A slug is immutable
        /// through the normal paths and other identities resolve against it, so nothing else corrects
        /// one.
        /// </remarks>
        private async Task StampAppSlugsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            foreach (var app in BuiltinApps.All)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var changed = await appRegistrationService.ApplyTreeSlugAsync(app.AppId, app.AppSlug, odinContext);
                if (changed)
                {
                    logger.LogDebug("v13->v14: app {app} is now {slug}", app.Name, app.AppSlug);
                }
            }
        }

        /// <summary>
        /// Every drive, circle and app the tree names that exists on this identity matches it.
        /// </summary>
        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            foreach (var request in BuiltinApps.AllDrives)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var drive = await driveManager.GetDriveAsync(request.TargetDrive.Alias);
                if (drive == null)
                {
                    continue;
                }

                if (drive.AppId != request.AppId || drive.DriveSlug != request.DriveSlug ||
                    drive.DriveTypeSlug != request.DriveTypeSlug)
                {
                    throw new OdinSystemException(
                        $"v13->v14 left drive {request.Name} disagreeing with the tree: " +
                        $"app {drive.AppId} slug '{drive.DriveSlug}' type '{drive.DriveTypeSlug}'");
                }
            }

            foreach (var def in BuiltinApps.AllCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var circle = await circleDefinitionService.GetCircleAsync(def.Id);
                if (circle == null)
                {
                    continue;
                }

                if (circle.AppId != def.AppId || circle.GrantOn != def.GrantOn ||
                    circle.Designation != def.Designation)
                {
                    throw new OdinSystemException(
                        $"v13->v14 left circle {def.Name} disagreeing with the tree: " +
                        $"app {circle.AppId} grantOn {circle.GrantOn}");
                }
            }

            var channels = await driveManager.GetDrivesAsync(
                SystemDriveConstants.ChannelDriveType, PageOptions.All, odinContext);

            foreach (var drive in channels.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (drive.AppId == null || string.IsNullOrWhiteSpace(drive.DriveSlug) ||
                    drive.DriveTypeSlug != BuiltinDrives.ChannelDriveTypeSlug)
                {
                    throw new OdinSystemException(
                        $"v13->v14 left channel drive {drive.Name} without an owning app, or with slug " +
                        $"'{drive.DriveSlug}' / type '{drive.DriveTypeSlug}'");
                }
            }

            var everyDrive = await driveManager.GetDrivesAsync(PageOptions.All, odinContext);
            var noSlug = everyDrive.Results.Where(d => string.IsNullOrWhiteSpace(d.DriveSlug)).ToList();
            if (noSlug.Count != 0)
            {
                throw new OdinSystemException(
                    $"v13->v14 left {noSlug.Count} drive(s) with no slug: " +
                    string.Join(", ", noSlug.Select(d => d.Name)));
            }

            var unownedDupes = everyDrive.Results
                .Where(d => d.AppId == null && !string.IsNullOrWhiteSpace(d.DriveSlug))
                .GroupBy(d => d.DriveSlug).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (unownedDupes.Count != 0)
            {
                throw new OdinSystemException(
                    "v13->v14 produced duplicate slugs among drives with no owning app, which the unique " +
                    "index cannot catch: " + string.Join(", ", unownedDupes));
            }

            foreach (var app in BuiltinApps.All)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var reg = await appRegistrationService.GetAppRegistration(app.AppId, odinContext);
                if (reg != null && reg.AppSlug != app.AppSlug)
                {
                    throw new OdinSystemException(
                        $"v13->v14 left app {app.Name} as '{reg.AppSlug}', not '{app.AppSlug}'");
                }
            }
        }
    }
}
