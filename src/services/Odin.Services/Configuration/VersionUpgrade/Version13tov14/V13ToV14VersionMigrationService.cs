using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Apps.Builtin;
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
        CircleDefinitionService circleDefinitionService,
        BuiltinProvisioner builtinProvisioner)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            await StampDrivesAsync(odinContext, cancellationToken);
            await StampCirclesAsync(cancellationToken);

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

                if (drive.AppId != null)
                {
                    continue;
                }

                if (request.AppId == null)
                {
                    throw new OdinSystemException(
                        $"Drive {request.Name} is in the tree with no owning app; every drive must have one");
                }

                logger.LogDebug("v13->v14: stamping {drive} as {slug} owned by {appId}",
                    request.Name, request.DriveSlug, request.AppId);

                await driveManager.StampDriveAddressAsync(drive.Id, request.AppId.Value,
                    request.DriveSlug, request.DriveTypeSlug, odinContext);
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

                await circleDefinitionService.StampOwningAppAsync(
                    def.Id, def.AppId.Value, def.GrantOn, def.Designation);
            }
        }

        /// <summary>
        /// Every drive and circle the tree names that exists on this identity has an owner.
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

                if (drive.AppId == null || string.IsNullOrEmpty(drive.DriveSlug))
                {
                    throw new OdinSystemException(
                        $"v13->v14 left drive {request.Name} without an owning app or slug");
                }
            }

            foreach (var def in BuiltinApps.AllCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var circle = await circleDefinitionService.GetCircleAsync(def.Id);
                if (circle != null && circle.AppId == null)
                {
                    throw new OdinSystemException(
                        $"v13->v14 left circle {def.Name} without an owning app");
                }
            }

            var builtin = BuiltinApps.Builtin.Select(a => a.Name).ToList();
            logger.LogDebug("v13->v14 validated; built-in apps: {apps}", string.Join(", ", builtin));
        }
    }
}
