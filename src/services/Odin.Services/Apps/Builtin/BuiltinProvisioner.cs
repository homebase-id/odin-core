using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Apps.Builtin;

/// <summary>
/// Provisions what an identity starts with: the built-in apps, their drives and their circles.
/// </summary>
/// <remarks>
/// Driven by <see cref="BuiltinApps"/>, so the set provisioned is a projection of the tree rather than a
/// list restated here.
/// <para>
/// <b>Not only first-run.</b>  <c>TenantConfigService.EnsureInitialOwnerSetupAsync</c> calls
/// <see cref="EnsureAllAsync"/>, but the version ladder calls <see cref="EnsureDrivesAsync"/> on its own
/// -- <c>VersionUpgradeService</c> does a single up-front pass so migrations can assume every drive of an
/// installed app exists rather than each creating what it needs.  Everything here is idempotent for that reason.
/// </para>
/// <para>
/// <b>Except what the owner uninstalled</b> (#2138): every step skips an app in <see cref="UninstalledBuiltinApps"/>,
/// the drives and circles it owns, and other apps' grants on those drives, so an upgrade doesn't bring it back.
/// </para>
/// </remarks>
public class BuiltinProvisioner(
    ILogger<BuiltinProvisioner> logger,
    IDriveManager driveManager,
    CircleDefinitionService circleDefinitionService,
    IAppRegistrationService appRegistrationService,
    UninstalledBuiltinApps uninstalledApps)
{
    /// <summary>
    /// Drives that must exist even though their app is not built-in, because the Chat app
    /// registration grants them to their circle members and issuing a grant for an absent drive throws
    /// (<c>ExchangeGrantService</c> resolves with <c>failIfInvalid: true</c>).
    /// </summary>
    /// <remarks>
    /// <b>Temporary.</b>  This is not an ownership fact, which is why the tree cannot express it.  The
    /// system circles that first needed it retired in #1809; it goes when those registrations stop
    /// naming these drives.
    /// </remarks>
    private static readonly IReadOnlyList<CreateDriveRequest> CarryOverDrives =
    [
        BuiltinDrives.ListsDrive,
        BuiltinDrives.MomentsDrive,

        // Mail joined this list when its app left BuiltinApps.Builtin and its registration still granted
        // MailDrive.  Nothing registers Mail any more; dropping the drive would change what a new identity
        // is given, so that is a decision of its own rather than part of this list's upkeep.
        BuiltinDrives.MailDrive
    ];

    /// <summary>The apps whose drives an identity is given: the built-in ones, and the owners of the carry-overs.</summary>
    public static bool IsProvisioned(Guid appId) => ProvisionedAppIds.Contains(appId);

    private static readonly HashSet<Guid> ProvisionedAppIds =
    [
        ..BuiltinApps.BuiltinAppIds,
        ..CarryOverDrives.Select(d => d.AppId!.Value)
    ];

    /// <summary>The owning app of every drive this provisions or grants, by alias.</summary>
    private static readonly Dictionary<Guid, Guid> OwningAppOfDrive = BuiltinApps.AllDrives
        .Concat(CarryOverDrives)
        .DistinctBy(d => d.TargetDrive.Alias.Value)
        .ToDictionary(d => d.TargetDrive.Alias.Value, d => d.AppId!.Value);

    /// <summary>True when one of <paramref name="appIds"/> owns this built-in drive.</summary>
    public static bool IsOwnedByAny(TargetDrive drive, IReadOnlySet<Guid> appIds) =>
        OwningAppOfDrive.TryGetValue(drive.Alias.Value, out var appId) && appIds.Contains(appId);

    /// <summary>
    /// The registration for each built-in app.  Not on the tree yet: a registration carries
    /// <c>AuthorizedCircles</c> and a circle-member grant, which the tree does not model.
    /// </summary>
    private static readonly IReadOnlyDictionary<Guid, AppRegistrationRequest> Registrations =
        new Dictionary<Guid, AppRegistrationRequest>
        {
            [SystemAppConstants.ChatAppId] = SystemAppConstants.ChatAppRegistrationRequest,
            [SystemAppConstants.FeedAppId] = SystemAppConstants.FeedAppRegistrationRequest,
            [SystemAppConstants.ContactsAppId] = SystemAppConstants.ContactsAppRegistrationRequest,
            [SystemAppConstants.EmailAppId] = SystemAppConstants.EmailAppRegistrationRequest,
            [SystemAppConstants.HomePageAppId] = SystemAppConstants.HomePageAppRegistrationRequest,
            [SystemAppConstants.LocationAppId] = SystemAppConstants.LocationAppRegistrationRequest,
            [SystemAppConstants.RecoveryAppId] = SystemAppConstants.RecoveryAppRegistrationRequest,
            [SystemAppConstants.SystemAppId] = SystemAppConstants.SystemAppRegistrationRequest,
            [SystemAppConstants.WebdropAppId] = SystemAppConstants.WebdropAppRegistrationRequest,
            [SystemAppConstants.MomentsAppId] = SystemAppConstants.MomentsAppRegistrationRequest,
            [SystemAppConstants.VaultAppId] = SystemAppConstants.VaultAppRegistrationRequest
        };

    /// <summary>
    /// Everything, in the one order that works.
    /// </summary>
    /// <remarks>
    /// The order is load-bearing, and each step is why the next can succeed:
    /// <list type="number">
    /// <item><b>Drives, non-anonymous first.</b>  The order the system circles needed, when creating an
    /// anonymous-read drive granted them read on it (retired, #1809).  Harmless now, and kept.</item>
    /// <item><b>Circles after drives.</b>  A circle that enrols ambiently is checked for deposit-only
    /// grants when it is written, and that check reads the drive to see whether it allows anonymous
    /// reads.  Creating circles first meant that lookup found nothing, so a read grant on an ambient
    /// circle failed with a message blaming the grant rather than the ordering.</item>
    /// <item><b>Apps last.</b>  A registration is granted drives, and a grant cannot be issued for a
    /// drive that does not exist.</item>
    /// </list>
    /// </remarks>
    public async Task EnsureAllAsync(IOdinContext odinContext)
    {
        await EnsureDrivesAsync(odinContext);
        await EnsureCirclesAsync(odinContext);
        await EnsureAppsAsync(odinContext);
    }

    /// <summary>The built-in zero-knowledge drives the identity doesn't have yet, for the owner's browser to create.</summary>
    public async Task<List<CreateDriveRequest>> GetMissingZeroKnowledgeDrivesAsync()
    {
        var uninstalled = await uninstalledApps.GetAsync();
        var missing = new List<CreateDriveRequest>();
        foreach (var drive in BuiltinApps.BrowserMadeDrives.Where(d => !uninstalled.Contains(d.AppId!.Value)))
        {
            if (await driveManager.GetDriveAsync(drive.TargetDrive.Alias) == null)
            {
                missing.Add(drive);
            }
        }

        return missing;
    }

    /// <summary>
    /// The built-in apps whose zero-knowledge drive the browser hasn't created yet: their circles and registration wait
    /// for it, since a grant can't be issued for a drive that isn't there.
    /// </summary>
    private async Task<HashSet<Guid>> AppsWaitingForTheirDriveAsync()
    {
        var missing = (await GetMissingZeroKnowledgeDrivesAsync()).Select(d => d.TargetDrive.Alias.Value).ToHashSet();
        return BuiltinApps.Builtin
            .Where(app => app.Drives.Any(d => missing.Contains(d.TargetDrive.Alias.Value)))
            .Select(app => app.AppId)
            .ToHashSet();
    }

    /// <summary>
    /// Creates the drives of every built-in app, plus the carry-overs, but those of an app the owner uninstalled.
    /// Idempotent.
    /// </summary>
    public async Task EnsureDrivesAsync(IOdinContext odinContext)
    {
        var uninstalled = await uninstalledApps.GetAsync();
        await EnsureDrivesAsync(DrivesMadeAtSetup.Where(d => !uninstalled.Contains(d.AppId!.Value)), odinContext);
    }

    /// <summary>
    /// What setup creates: the built-in drives, but the ones the owner's browser creates
    /// (<see cref="GetMissingZeroKnowledgeDrivesAsync"/>, after which their app and circles follow), plus the
    /// carry-overs.
    /// </summary>
    private static IEnumerable<CreateDriveRequest> DrivesMadeAtSetup => BuiltinApps.DrivesMadeAtSetup
        .Concat(CarryOverDrives)
        .DistinctBy(d => d.TargetDrive.Alias.Value);

    private async Task EnsureDrivesAsync(IEnumerable<CreateDriveRequest> requests, IOdinContext odinContext)
    {
        // Non-anonymous first -- see EnsureAllAsync for why.
        foreach (var request in requests.OrderBy(d => d.AllowAnonymousReads))
        {
            AssertAddressed(request);

            if (await driveManager.GetDriveAsync(request.TargetDrive.Alias) != null)
            {
                continue;
            }

            // Named before and after, not just after: creating a drive publishes
            // DriveDefinitionAddedNotification, and its handlers re-grant circles to every existing
            // member.  That is the slow part, and when it stalls the only clue is which drive was
            // being created -- so the drive has to be in the log before the work starts.
            logger.LogDebug("Creating drive '{drive}' ({alias})", request.Name, request.TargetDrive.Alias);
            await driveManager.CreateDriveAsync(request, odinContext);
            logger.LogDebug("Created drive '{drive}'", request.Name);
        }
    }

    /// <summary>
    /// Creates the circles owned by every built-in app, but one the owner uninstalled.  Idempotent.
    /// </summary>
    public async Task EnsureCirclesAsync(IOdinContext odinContext)
    {
        var skipped = (await AppsWaitingForTheirDriveAsync()).Union(await uninstalledApps.GetAsync()).ToHashSet();
        foreach (var def in BuiltinApps.SeededCircles.Where(c => c.AppId is not { } appId || !skipped.Contains(appId)))
        {
            await circleDefinitionService.EnsureCircleExistsAsync(def);
        }
    }

    /// <summary>
    /// Nothing is provisioned without an address.  A drive that reaches creation with no slug gets one
    /// derived from its display name, which is how the Location app came to be registered as
    /// <c>homebase-locat</c> -- a permanent address nobody chose, because a slug is immutable once
    /// written.  Failing here is the cheaper outcome.
    /// </summary>
    private static void AssertAddressed(CreateDriveRequest request)
    {
        if (request.AppId == null)
        {
            throw new OdinSystemException($"Drive '{request.Name}' has no owning app");
        }

        if (!request.DriveSlug.HasValue || !request.DriveTypeSlug.HasValue)
        {
            throw new OdinSystemException(
                $"Drive '{request.Name}' has no slug ('{request.DriveSlug}' / " +
                $"'{request.DriveTypeSlug}'); it cannot be provisioned");
        }
    }

    /// <summary>
    /// A built-in registration as this identity can be given it: without its grants on the drives of an app the owner
    /// uninstalled, which aren't there.  For a migration re-applying one.
    /// </summary>
    public async Task<AppRegistrationRequest> WithoutUninstalledDrivesAsync(AppRegistrationRequest request) =>
        WithoutDrivesOf(request, await uninstalledApps.GetAsync(), request.AppSlug);

    /// <summary>
    /// The registration to make, with this slug: the constants carry no slug, so registering one as-is derives the slug
    /// from the display name.  The tree is the authority, so it is applied here.  And no grant on a drive
    /// whose app the owner uninstalled: that drive isn't there, and issuing a grant for it throws.
    /// </summary>
    /// <remarks>
    /// Copied rather than assigned: the constants are shared statics, and provisioning runs per tenant.
    /// </remarks>
    private static AppRegistrationRequest WithoutDrivesOf(AppRegistrationRequest request, IReadOnlySet<Guid> uninstalled,
        OdinSlug appSlug) => new()
    {
        AppId = request.AppId,
        Name = request.Name,
        AppSlug = appSlug,
        CorsHostName = request.CorsHostName,
        PermissionSet = request.PermissionSet,
        Drives = request.Drives?.Where(g => !IsOwnedByAny(g.PermissionedDrive.Drive, uninstalled)).ToList(),
        AuthorizedCircles = request.AuthorizedCircles,
        CircleMemberPermissionGrant = request.CircleMemberPermissionGrant == null
            ? null
            : new PermissionSetGrantRequest
            {
                PermissionSet = request.CircleMemberPermissionGrant.PermissionSet,
                Drives = request.CircleMemberPermissionGrant.Drives?
                    .Where(g => !IsOwnedByAny(g.PermissionedDrive.Drive, uninstalled)).ToList()
            }
    };

    /// <summary>
    /// Registers every built-in app, but one the owner uninstalled.  Idempotent.
    /// </summary>
    public async Task EnsureAppsAsync(IOdinContext odinContext)
    {
        var uninstalled = await uninstalledApps.GetAsync();
        var skipped = (await AppsWaitingForTheirDriveAsync()).Union(uninstalled).ToHashSet();
        foreach (var app in BuiltinApps.Builtin.Where(a => !skipped.Contains(a.AppId)))
        {
            await EnsureAppAsync(app, uninstalled, odinContext);
        }
    }

    /// <summary>
    /// The owner is installing <paramref name="appId"/> again, after uninstalling it: its drives and circles are made
    /// as on a new identity, so the registration being made can be granted them.  True when it was uninstalled; the
    /// registration takes it off <see cref="UninstalledBuiltinApps"/> once it is saved, so a failed one leaves it off
    /// the provisioner's list still.
    /// </summary>
    /// <remarks>
    /// Its browser-made drives are left to the owner's browser, which <see cref="GetMissingZeroKnowledgeDrivesAsync"/>
    /// lists them to once it is registered; its circles wait for them, as at setup.  A drive whose delete is still
    /// being purged can't be made again yet, which refuses the install with that reason.
    /// </remarks>
    public async Task<bool> PrepareReinstallAsync(Guid appId, IOdinContext odinContext)
    {
        if (!(await uninstalledApps.GetAsync()).Contains(appId))
        {
            return false;
        }

        await EnsureDrivesAsync(DrivesMadeAtSetup.Where(d => d.AppId == appId), odinContext);

        var browserMade = BuiltinApps.BrowserMadeDrives.Where(d => d.AppId == appId);
        foreach (var drive in browserMade)
        {
            if (await driveManager.GetDriveAsync(drive.TargetDrive.Alias) == null)
            {
                return true; // its circles follow the drive
            }
        }

        foreach (var def in BuiltinApps.SeededCircles.Where(c => c.AppId == appId))
        {
            await circleDefinitionService.EnsureCircleExistsAsync(def);
        }

        return true;
    }

    /// <summary>
    /// After <paramref name="appId"/> is installed again: the grants the other built-in registrations declare on its
    /// drives, which its uninstall took off them (Chat's on Moments, Lists and Location), are given back, as a new
    /// identity has them.  Only on drives that exist, and not to a revoked app, whose grant this would rebuild.
    /// </summary>
    public async Task RestoreGrantsOnItsDrivesAsync(Guid appId, IOdinContext odinContext)
    {
        var itsDrives = new List<TargetDrive>();
        foreach (var (alias, owner) in OwningAppOfDrive.Where(kv => kv.Value == appId))
        {
            var drive = await driveManager.GetDriveAsync(alias);
            if (drive != null)
            {
                itsDrives.Add(drive.TargetDriveInfo);
            }
        }

        if (itsDrives.Count == 0)
        {
            return;
        }

        foreach (var (otherAppId, request) in Registrations.Where(r => r.Key != appId))
        {
            var registration = await appRegistrationService.GetAppRegistration(otherAppId, odinContext);
            if (registration == null || registration.IsRevoked)
            {
                continue;
            }

            var held = registration.Grant.DriveGrants.Select(g => new DriveGrantRequest { PermissionedDrive = g.PermissionedDrive })
                .ToList();
            var missing = Missing(request.Drives, held, itsDrives);
            if (missing.Count > 0)
            {
                await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
                {
                    AppId = otherAppId,
                    PermissionSet = registration.Grant.PermissionSet,
                    Drives = [..held, ..missing]
                }, odinContext);
            }

            var memberHeld = registration.CircleMemberPermissionSetGrantRequest?.Drives?.ToList() ?? [];
            var memberMissing = Missing(request.CircleMemberPermissionGrant?.Drives, memberHeld, itsDrives);
            if (memberMissing.Count > 0)
            {
                await appRegistrationService.UpdateAuthorizedCirclesAsync(new UpdateAuthorizedCirclesRequest
                {
                    AppId = otherAppId,
                    AuthorizedCircles = registration.AuthorizedCircles ?? [],
                    CircleMemberPermissionGrant = new PermissionSetGrantRequest
                    {
                        PermissionSet = registration.CircleMemberPermissionSetGrantRequest?.PermissionSet ?? new PermissionSet(),
                        Drives = [..memberHeld, ..memberMissing]
                    }
                }, odinContext);
            }
        }
    }

    /// <summary>The declared grants on <paramref name="drives"/> that <paramref name="held"/> has none for.</summary>
    private static List<DriveGrantRequest> Missing(IEnumerable<DriveGrantRequest> declared, List<DriveGrantRequest> held,
        List<TargetDrive> drives) =>
        (declared ?? [])
        .Where(g => drives.Contains(g.PermissionedDrive.Drive) && held.All(h => h.PermissionedDrive.Drive != g.PermissionedDrive.Drive))
        .ToList();

    /// <summary>
    /// Refuses to create a browser-made built-in drive (Vault, Email) whose app the owner uninstalled: it would be a
    /// system drive no app owns, which nothing could delete.  Installing the app again comes first.
    /// </summary>
    public async Task AssertNotOfAnUninstalledAppAsync(TargetDrive drive)
    {
        if (IsOwnedByAny(drive, await uninstalledApps.GetAsync()))
        {
            throw new OdinClientException("The app that owns this drive was uninstalled; install it again first");
        }
    }

    /// <summary>
    /// The owner's browser just created <paramref name="driveId"/>: if it is a browser-made built-in drive, its app's
    /// circles and registration, which waited for it, follow once none of that app's browser-made drives is missing.
    /// Nothing for any other drive.
    /// </summary>
    public async Task EnsureAppOfBrowserMadeDriveAsync(Guid driveId, IOdinContext odinContext)
    {
        var app = BuiltinApps.Builtin.SingleOrDefault(a =>
            a.Drives.Any(d => d.SharingModel.IsZeroKnowledgeDrive() && d.TargetDrive.Alias.Value == driveId));
        var uninstalled = await uninstalledApps.GetAsync();
        if (app == null || uninstalled.Contains(app.AppId))
        {
            return;
        }

        foreach (var drive in app.Drives.Where(d => d.SharingModel.IsZeroKnowledgeDrive()))
        {
            if (await driveManager.GetDriveAsync(drive.TargetDrive.Alias) == null)
            {
                return; // another of its drives is still to come
            }
        }

        foreach (var def in app.Circles)
        {
            await circleDefinitionService.EnsureCircleExistsAsync(def);
        }

        await EnsureAppAsync(app, uninstalled, odinContext);
    }

    private async Task EnsureAppAsync(WellknownAppDefinition app, IReadOnlySet<Guid> uninstalled, IOdinContext odinContext)
    {
        if (!Registrations.TryGetValue(app.AppId, out var request))
        {
            throw new OdinSystemException(
                $"App '{app.Name}' is built-in but has no registration request");
        }

        if (await appRegistrationService.GetAppRegistration(request.AppId, odinContext) != null)
        {
            return;
        }

        if (!app.AppSlug.HasValue)
        {
            throw new OdinSystemException(
                $"App '{app.Name}' has no slug; it cannot be registered");
        }

        logger.LogDebug("Registering built-in app {app} as {slug}", app.Name, app.AppSlug);
        await appRegistrationService.RegisterAppAsync(WithoutDrivesOf(request, uninstalled, app.AppSlug), odinContext);
    }
}
