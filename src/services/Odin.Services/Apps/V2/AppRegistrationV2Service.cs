#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version12tov13;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;
using Codes = Odin.Services.Apps.V2.AppRegistrationProblemCodes;

namespace Odin.Services.Apps.V2;

/// <summary>
/// V2 app registration: an app declares the drives and circles it owns, and the registration, the
/// owned resources and later changes to them go through here (docs/app-registration-v2-plan-simplified.md).
/// </summary>
/// <remarks>
/// Additive over V1.  Every write goes through an existing public method -- <c>RegisterAppAsync</c>,
/// <c>UpdateAppPermissionsAsync</c>, <c>CreateDriveAsync</c>, <c>CreateCircleDefinitionAsync</c>,
/// <c>UpdateCircleDefinitionAsync</c> -- so V2 has exactly V1's side effects and none of its own.
/// <para>
/// Work is split into a <see cref="AppRegistrationPlan"/> (validate against one snapshot of what exists,
/// collect every problem, compute the resulting grants) and applying plans (the writes).  All
/// client-caused failures surface in the plan, before the first write.  After that nothing here is
/// transactional end to end, so each write is safe to retry instead.
/// </para>
/// </remarks>
public class AppRegistrationV2Service(
    IAppRegistrationService appRegistrationService,
    DriveManager driveManager,
    CircleDefinitionService circleDefinitionService,
    CircleMembershipService circleMembershipService,
    CircleNetworkService circleNetworkService,
    LegacyDefinitionStore legacyStore,
    IMediator mediator)
{
    private const DrivePermission OwnedDrivePermission = DrivePermission.ReadWrite;

    /// <summary>
    /// Apps V2 may not register or change: the tree apps, whose drives, circles and registrations the
    /// provisioner re-applies on every upgrade, and Mail, which owns tree drives and circles without a
    /// <see cref="BuiltinApps"/> entry.
    /// </summary>
    public static bool IsReserved(Guid appId)
    {
        return BuiltinApps.Get(appId) != null || appId == SystemAppConstants.MailAppId;
    }

    // ============================================================================================
    // Reads
    // ============================================================================================

    public async Task<List<AppRegistrationV2>> GetAllAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var apps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
        var drives = (await driveManager.GetDrivesAsync(PageOptions.All, odinContext)).Results.ToLookup(d => d.AppId);
        var circles = (await circleDefinitionService.GetCirclesAsync(includeSystemCircle: false)).ToLookup(c => c.AppId);

        return apps
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(app => Compose(app, drives[app.AppId.Value], circles[app.AppId.Value]))
            .ToList();
    }

    public async Task<AppRegistrationV2?> GetAsync(Guid appId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var app = await appRegistrationService.GetAppRegistration(appId, odinContext);
        if (app == null)
        {
            return null;
        }

        var drives = await driveManager.GetDrivesByAppIdAsync(appId, odinContext);
        var circles = (await circleDefinitionService.GetCirclesAsync(includeSystemCircle: false)).Where(c => c.AppId == appId);
        return Compose(app, drives, circles);
    }

    // ============================================================================================
    // Validate / register / apply
    // ============================================================================================

    /// <summary>
    /// Every problem with the manifest, and what applying it would change.  Install wording when the app
    /// is not registered, update wording when it is.  Writes nothing.
    /// </summary>
    public async Task<AppRegistrationValidationResult> ValidateAsync(AppManifestV2 manifest, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(manifest, nameof(manifest));
        return (await PlanAsync([manifest], ValidationMode.InstallOrUpdate, odinContext)).Single().ToResult();
    }

    public async Task<AppRegistrationV2> RegisterAsync(AppManifestV2 manifest, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(manifest, nameof(manifest));
        var plans = await PlanAsync([manifest], ValidationMode.Install, odinContext);
        AppRegistrationProblem.ThrowIfAny(plans.Single().Problems);

        await ApplyPlansAsync(plans, odinContext);
        return (await GetAsync(manifest.AppId, odinContext))!;
    }

    /// <summary>
    /// Makes the app match <paramref name="manifest"/>: registers it when it is new, otherwise creates
    /// the owned drives and circles it lacks and replaces its permissions and authorized circles.
    /// </summary>
    /// <remarks>
    /// The manifest is the desired state, so access it leaves out is removed -- the same replace semantics
    /// as the V1 update methods.  Owned drives and circles whose settings differ are refused by validation
    /// rather than changed; those go through the owned-drive and owned-circle updates.
    /// </remarks>
    public async Task<AppRegistrationV2> ApplyAsync(AppManifestV2 manifest, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(manifest, nameof(manifest));
        var plans = await PlanAsync([manifest], ValidationMode.InstallOrUpdate, odinContext);
        AppRegistrationProblem.ThrowIfAny(plans.Single().Problems);

        await ApplyPlansAsync(plans, odinContext);
        return (await GetAsync(manifest.AppId, odinContext))!;
    }

    /// <summary>
    /// Validates several manifests as one request, against one snapshot of what exists.  Each may grant
    /// drives, and authorize circles, that another manifest in the request declares; claims on the same
    /// slug, drive or circle by two manifests are problems.  Writes nothing.
    /// </summary>
    internal async Task<List<AppRegistrationPlan>> PlanAsync(IReadOnlyList<AppManifestV2> manifests, ValidationMode mode,
        IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var snapshot = new Snapshot
        {
            Drives = (await driveManager.GetDrivesAsync(PageOptions.All, odinContext)).Results.ToDictionary(d => d.Id),
            Circles = (await circleDefinitionService.GetCirclesAsync(includeSystemCircle: true)).ToDictionary(c => c.Id.Value),
            Apps = (await appRegistrationService.GetRegisteredAppsAsync(odinContext)).ToDictionary(a => a.AppId.Value),
            IsPreMove = await legacyStore.IsPreMoveAsync(),
            Manifests = manifests
        };

        return manifests.Select(m => Plan(m, mode, snapshot)).ToList();
    }

    /// <summary>
    /// Writes validated plans: every plan's drives, then every plan's circles (either may be granted by
    /// another plan), then each registration.  Callers must have checked the plans have no problems.
    /// </summary>
    internal async Task ApplyPlansAsync(IReadOnlyList<AppRegistrationPlan> plans, IOdinContext odinContext)
    {
        await CreateOwnedDrivesAsync(plans.SelectMany(p => p.Diff.DrivesToCreate.Select(d => (p.AppId, d))).ToList(), odinContext);

        foreach (var plan in plans)
        {
            await CreateOwnedCirclesAsync(plan.AppId, plan.Diff.CirclesToCreate, odinContext);
        }

        foreach (var plan in plans)
        {
            var m = plan.Manifest;
            if (plan.Existing == null)
            {
                await appRegistrationService.RegisterAppAsync(new AppRegistrationRequest
                {
                    AppId = m.AppId,
                    Name = m.Name,
                    AppSlug = m.AppSlug,
                    CorsHostName = string.IsNullOrWhiteSpace(m.CorsHostName) ? null! : m.CorsHostName,
                    PermissionSet = m.PermissionSet ?? new PermissionSet(),
                    Drives = plan.FinalGrants,
                    AuthorizedCircles = m.AuthorizedCircles ?? [],
                    CircleMemberPermissionGrant = m.CircleMemberPermissionGrant ?? new PermissionSetGrantRequest()
                }, odinContext);
                continue;
            }

            if (plan.Diff.HasGrantChanges)
            {
                await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
                {
                    AppId = m.AppId,
                    PermissionSet = plan.PermissionSet ?? new PermissionSet(),
                    Drives = plan.FinalGrants
                }, odinContext);
            }

            if (plan.UpdatesAuthorizedCircles)
            {
                await UpdateAuthorizedCirclesIfChangedAsync(plan.Existing, m.AuthorizedCircles, m.CircleMemberPermissionGrant,
                    odinContext);
            }
        }
    }

    // ============================================================================================
    // Updates
    // ============================================================================================

    /// <summary>
    /// Creates owned drives and circles for an installed app, in one call, then re-grants the app.
    /// </summary>
    public async Task<AppRegistrationV2> AddOwnedAsync(Guid appId, AddOwnedResourcesRequest request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        var app = await GetUpdatableAppAsync(appId, odinContext);

        var plans = await PlanAsync([new AppManifestV2
        {
            AppId = appId,
            Name = app.Name,
            AppSlug = app.AppSlug,
            CorsHostName = app.CorsHostName,
            OwnedDrives = request.OwnedDrives,
            OwnedCircles = request.OwnedCircles
        }], ValidationMode.AddOwned, odinContext);
        AppRegistrationProblem.ThrowIfAny(plans.Single().Problems);

        await ApplyPlansAsync(plans, odinContext);
        return (await GetAsync(appId, odinContext))!;
    }

    /// <summary>
    /// Replaces the app's permission keys and explicit drive grants.  Owned drives and the drives of
    /// owned circles are merged back in, so the app keeps them unless an explicit grant says otherwise.
    /// </summary>
    public async Task UpdatePermissionsAsync(Guid appId, UpdateAppPermissionsV2Request request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        var app = await GetUpdatableAppAsync(appId, odinContext);

        var explicitGrants = request.Drives ?? [];
        for (var i = 0; i < explicitGrants.Count; i++)
        {
            var drive = explicitGrants[i]?.PermissionedDrive?.Drive;
            if (drive == null || !drive.IsValid() || await driveManager.GetDriveAsync(drive.Alias) == null)
            {
                throw new OdinClientException($"drives[{i}] does not name an existing drive",
                    OdinClientErrorCode.InvalidGrantNonExistingDrive);
            }
        }

        await RebuildGrantsAsync(app, explicitGrants, request.PermissionSet, odinContext);
    }

    /// <summary>
    /// Replaces the app's authorized circles.  Skipped entirely when nothing changed, because the V1
    /// method re-grants every member of every authorized circle on each call.
    /// </summary>
    public async Task UpdateAuthorizedCirclesAsync(Guid appId, UpdateAuthorizedCirclesV2Request request,
        IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        var app = await GetUpdatableAppAsync(appId, odinContext);

        foreach (var circleId in request.AuthorizedCircles ?? [])
        {
            if (circleId == Guid.Empty || await circleDefinitionService.GetCircleAsync(circleId) == null)
            {
                throw new OdinClientException($"Circle {circleId} does not exist", OdinClientErrorCode.CircleNotFound);
            }
        }

        await UpdateAuthorizedCirclesIfChangedAsync(app, request.AuthorizedCircles, request.CircleMemberPermissionGrant,
            odinContext);
    }

    public async Task UpdateOwnedDriveAsync(Guid appId, Guid driveId, UpdateOwnedDriveRequest request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        await GetUpdatableAppAsync(appId, odinContext);

        var drive = AssertOwnedBy(await driveManager.GetDriveAsync(driveId), d => d.AppId, appId, $"Drive {driveId}");

        // Checked up front so no setter refuses after another has already written.
        if (drive.OwnerOnly && (request.AllowAnonymousReads == true || request.AllowSubscriptions == true))
        {
            throw new OdinClientException("An owner-only drive cannot allow anonymous reads or subscriptions",
                OdinClientErrorCode.CannotAllowAnonymousReadsOnOwnerOnlyDrive);
        }

        if (request.AllowAnonymousReads is { } anon && anon != drive.AllowAnonymousReads)
        {
            await driveManager.SetDriveReadModeAsync(driveId, anon, odinContext);
        }

        if (request.AllowSubscriptions is { } subs && subs != drive.AllowSubscriptions)
        {
            await driveManager.SetDriveAllowSubscriptionsAsync(driveId, subs, odinContext);
        }

        if (request.AllowCdn is { } cdn && cdn != drive.IsCdnEnabled())
        {
            await driveManager.SetDriveAllowCdnAsync(driveId, cdn, odinContext);
        }

        if (request.IsArchived is { } archived && archived != drive.IsArchived)
        {
            await driveManager.SetArchiveDriveFlagAsync(driveId, archived, odinContext);
        }

        if (request.Metadata != null && request.Metadata != drive.Metadata)
        {
            await driveManager.UpdateMetadataAsync(driveId, request.Metadata, odinContext);
        }

        if (request.Attributes != null)
        {
            await driveManager.UpdateAttributesAsync(driveId, request.Attributes, odinContext);
        }
    }

    /// <summary>
    /// Updates an owned circle.  The route id wins; a body that names a different id is refused.
    /// Members are only re-granted when what they are granted changed.
    /// </summary>
    public async Task UpdateOwnedCircleAsync(Guid appId, Guid circleId, OwnedCircle circle, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(circle, nameof(circle));
        var app = await GetUpdatableAppAsync(appId, odinContext);

        if (circle.Id != Guid.Empty && circle.Id != circleId)
        {
            throw new OdinClientException("The circle id in the body does not match the route", OdinClientErrorCode.ArgumentError);
        }

        var existing = AssertOwnedBy(await circleDefinitionService.GetCircleAsync(circleId), c => c.AppId, appId, $"Circle {circleId}");

        // Validate fully before anything is written.  UpdateCircleDefinitionAsync rewrites member ICRs
        // before its own permission and deposit-only checks run.
        circle.Id = circleId;
        var plans = await PlanAsync([new AppManifestV2 { AppId = appId, Name = app.Name, AppSlug = app.AppSlug, OwnedCircles = [circle] }],
            ValidationMode.UpdateCircle, odinContext);
        AppRegistrationProblem.ThrowIfAny(plans.Single().Problems);

        var updated = new CircleDefinition
        {
            Id = existing.Id,
            Created = existing.Created,
            LastUpdated = existing.LastUpdated,
            Disabled = existing.Disabled,
            AppId = existing.AppId,
            Name = circle.Name,
            Description = circle.Description!,
            DriveGrants = circle.DriveGrants ?? [],
            Permissions = circle.Permissions ?? new PermissionSet(),
            GrantOn = circle.GrantOn,
            Designation = circle.Designation,
            Emoji = circle.Emoji!
        };

        await circleDefinitionService.AssertDepositOnlyIfAmbientAsync(updated);

        if (!SameGrants(existing.DriveGrants, updated.DriveGrants) || !SameKeys(existing.Permissions, updated.Permissions))
        {
            await circleNetworkService.UpdateCircleDefinitionAsync(updated, odinContext);
            await RebuildGrantsAsync(app, CurrentGrants(app), app.Grant?.PermissionSet, odinContext);
        }
        else
        {
            // Name, description, emoji, designation or GrantOn only: what members hold is unchanged, so
            // re-minting every member's grant would be wasted work.
            await circleMembershipService.UpdateAsync(updated, odinContext);
            await mediator.Publish(new CircleDefinitionChangedNotification
            {
                OdinContext = odinContext,
                CircleId = circleId,
                Change = CircleDefinitionChangeType.Updated,
            });
        }
    }

    public async Task DeleteOwnedCircleAsync(Guid appId, Guid circleId, IOdinContext odinContext)
    {
        var app = await GetUpdatableAppAsync(appId, odinContext);
        AssertOwnedBy(await circleDefinitionService.GetCircleAsync(circleId), c => c.AppId, appId, $"Circle {circleId}");

        if (app.AuthorizedCircles?.Contains(circleId) ?? false)
        {
            throw new OdinClientException(
                $"Circle {circleId} is one of this app's authorized circles; remove it there first",
                OdinClientErrorCode.ArgumentError);
        }

        // Refuses a circle with members.
        await circleNetworkService.DeleteCircleDefinitionAsync(circleId, odinContext);
    }

    // ============================================================================================
    // Writes
    // ============================================================================================

    /// <summary>
    /// Creates owned drives with two passes over the system circles in total, however many of them allow
    /// anonymous reads.
    /// </summary>
    /// <remarks>
    /// Creating an anonymous-read drive makes <c>CircleNetworkService.HandleDriveAdded</c> re-grant both
    /// system circles to every connection -- per drive.  Instead every drive is created private, the
    /// system circles are given all the new read grants in one update each, and only then are the drives
    /// opened; <c>HandleDriveUpdated</c> finds the grant already there and does nothing.
    /// </remarks>
    private async Task CreateOwnedDrivesAsync(List<(Guid appId, OwnedDrive drive)> drives, IOdinContext odinContext)
    {
        foreach (var (appId, d) in drives)
        {
            await driveManager.CreateDriveAsync(new CreateDriveRequest
            {
                Name = d.Name,
                TargetDrive = d.TargetDrive,
                Metadata = d.Metadata!,
                AllowAnonymousReads = false,
                AllowSubscriptions = d.AllowSubscriptions,
                AllowCdn = d.AllowCdn,
                OwnerOnly = d.OwnerOnly,
                AppId = appId,
                DriveSlug = d.DriveSlug,
                DriveTypeSlug = d.DriveTypeSlug,
                Attributes = d.Attributes!
            }, odinContext);
        }

        var anonymous = drives.Select(x => x.drive).Where(d => d.AllowAnonymousReads).ToList();
        if (anonymous.Count == 0)
        {
            return;
        }

        foreach (var systemCircleId in SystemCircleConstants.AllSystemCircles)
        {
            var def = await circleDefinitionService.GetCircleAsync(systemCircleId);
            if (def == null)
            {
                continue;
            }

            var grants = def.DriveGrants?.ToList() ?? [];
            var missing = anonymous.Where(d => grants.All(g => g.PermissionedDrive.Drive != d.TargetDrive)).ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            grants.AddRange(missing.Select(d => Grant(d.TargetDrive, DrivePermission.Read)));
            def.DriveGrants = grants;
            await circleNetworkService.UpdateCircleDefinitionAsync(def, odinContext);
        }

        foreach (var d in anonymous)
        {
            await driveManager.SetDriveReadModeAsync(d.TargetDrive.Alias, true, odinContext);
        }
    }

    private async Task CreateOwnedCirclesAsync(Guid appId, List<OwnedCircle> circles, IOdinContext odinContext)
    {
        foreach (var c in circles)
        {
            // The owner console's path, so CircleDefinitionChangedNotification fires.
            await circleMembershipService.CreateCircleDefinitionAsync(new CreateCircleRequest
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description!,
                DriveGrants = c.DriveGrants ?? [],
                Permissions = c.Permissions ?? new PermissionSet(),
                AppId = appId,
                GrantOn = c.GrantOn,
                Designation = c.Designation,
                Emoji = c.Emoji!
            }, odinContext);
        }
    }

    /// <summary>
    /// The grant rebuild from current state, for updates that do not go through a plan.  See
    /// <see cref="BuildGrants"/>.
    /// </summary>
    private async Task RebuildGrantsAsync(RedactedAppRegistration app, IEnumerable<DriveGrantRequest> explicitGrants,
        PermissionSet? permissionSet, IOdinContext odinContext)
    {
        var appId = app.AppId.Value;
        var ownedDrives = await driveManager.GetDrivesByAppIdAsync(appId, odinContext);
        var ownedCircles = (await circleDefinitionService.GetCirclesAsync(includeSystemCircle: false)).Where(c => c.AppId == appId);

        await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
        {
            AppId = appId,
            PermissionSet = permissionSet ?? new PermissionSet(),
            Drives = BuildGrants(explicitGrants, ownedDrives.Select(d => d.TargetDriveInfo), ownedCircles.SelectMany(c => c.DriveGrants ?? []))
        }, odinContext);
    }

    private async Task UpdateAuthorizedCirclesIfChangedAsync(RedactedAppRegistration app, List<Guid>? circles,
        PermissionSetGrantRequest? grant, IOdinContext odinContext)
    {
        circles ??= [];
        grant ??= new PermissionSetGrantRequest();
        if (SameCircles(app.AuthorizedCircles, circles) && SameMemberGrant(app.CircleMemberPermissionSetGrantRequest, grant))
        {
            return;
        }

        await appRegistrationService.UpdateAuthorizedCirclesAsync(new UpdateAuthorizedCirclesRequest
        {
            AppId = app.AppId,
            AuthorizedCircles = circles,
            CircleMemberPermissionGrant = grant
        }, odinContext);
    }

    /// <summary>
    /// An app's drive grants: explicit grants, plus ReadWrite on owned drives, plus whatever the app's owned
    /// circles grant.  Applied through the V1 method, which also adds the transient drive and resets the
    /// permission cache.
    /// </summary>
    /// <remarks>
    /// Owned circles are included because adopting or reassigning a circle adds its drives to the owning
    /// app's grant (<c>CircleNetworkService.GrantAppTheCirclesDrivesAsync</c>); rebuilding without them
    /// would silently take those away.
    /// </remarks>
    internal static List<DriveGrantRequest> BuildGrants(
        IEnumerable<DriveGrantRequest> explicitGrants,
        IEnumerable<TargetDrive> ownedDrives,
        IEnumerable<DriveGrantRequest> ownedCircleGrants)
    {
        var merged = new Dictionary<TargetDrive, PermissionedDrive>();

        foreach (var g in explicitGrants.Where(g => g?.PermissionedDrive?.Drive != null))
        {
            merged[g.PermissionedDrive.Drive] = g.PermissionedDrive.Clone();
        }

        foreach (var drive in ownedDrives)
        {
            merged.TryAdd(drive, new PermissionedDrive { Drive = drive, Permission = OwnedDrivePermission });
        }

        foreach (var g in ownedCircleGrants.Where(g => g?.PermissionedDrive?.Drive != null))
        {
            if (merged.TryGetValue(g.PermissionedDrive.Drive, out var current))
            {
                current.Permission |= g.PermissionedDrive.Permission;
                current.TemporalReadWindowSeconds ??= g.PermissionedDrive.TemporalReadWindowSeconds;
            }
            else
            {
                merged[g.PermissionedDrive.Drive] = g.PermissionedDrive.Clone();
            }
        }

        return merged.Values.Select(pd => new DriveGrantRequest { PermissionedDrive = pd }).ToList();
    }

    // ============================================================================================
    // Planning (validation)
    // ============================================================================================

    internal enum ValidationMode
    {
        /// <summary>Install when the app is not registered, update when it is.</summary>
        InstallOrUpdate,

        /// <summary>A registration; the app must not be registered.</summary>
        Install,

        /// <summary>Owned drives and circles for a registered app; nothing else in the manifest is read.</summary>
        AddOwned,

        /// <summary>A change to one owned circle, which the caller has checked exists and is owned.</summary>
        UpdateCircle
    }

    /// <summary>What exists, loaded once per request, plus every manifest in the request.</summary>
    private sealed class Snapshot
    {
        public required Dictionary<Guid, StorageDrive> Drives { get; init; }
        public required Dictionary<Guid, CircleDefinition> Circles { get; init; }
        public required Dictionary<Guid, RedactedAppRegistration> Apps { get; init; }
        public required bool IsPreMove { get; init; }
        public required IReadOnlyList<AppManifestV2> Manifests { get; init; }

        public string NameOf(Guid appId) =>
            Apps.TryGetValue(appId, out var app) ? app.Name
            : Manifests.FirstOrDefault(m => m.AppId == appId)?.Name ?? $"app {appId}";
    }

    private sealed record DriveFacts(TargetDrive TargetDrive, string Name, Guid? OwningAppId, bool OwnerOnly, bool AllowAnonymousReads);

    private AppRegistrationPlan Plan(AppManifestV2 m, ValidationMode mode, Snapshot snapshot)
    {
        var appId = m.AppId;
        var existing = appId == Guid.Empty ? null : snapshot.Apps.GetValueOrDefault(appId);
        var plan = new AppRegistrationPlan { Manifest = m, Existing = existing };
        var problems = plan.Problems;
        void Problem(string code, string subject, string message) => problems.Add(AppRegistrationProblem.Of(code, subject, message));

        var others = snapshot.Manifests.Where(o => !ReferenceEquals(o, m) && o.AppId != appId).ToList();
        var readsRegistration = mode is ValidationMode.Install or ValidationMode.InstallOrUpdate;

        // ----- the app -------------------------------------------------------------------------

        if (appId == Guid.Empty)
        {
            Problem(Codes.AppIdRequired, "appId", "An app id is required");
        }
        else if (IsReserved(appId))
        {
            Problem(Codes.ReservedApp, "appId", "This is a built-in app; it is managed by the identity, not V2");
        }

        if (snapshot.IsPreMove)
        {
            Problem(Codes.IdentityNotUpgraded, "", "This identity has not finished upgrading; try again shortly");
        }

        if (mode == ValidationMode.Install && existing != null)
        {
            Problem(Codes.AlreadyRegistered, "appId", "This app is already registered");
        }

        if (readsRegistration)
        {
            ValidateAppFields(m, existing, Problem);
            if (existing == null && OdinSlug.IsValid(m.AppSlug) &&
                (snapshot.Apps.Values.Any(a => a.AppSlug == m.AppSlug && a.AppId.Value != appId) || others.Any(o => o.AppSlug == m.AppSlug)))
            {
                Problem(Codes.SlugTaken, "appSlug", $"Another app already holds the slug '{m.AppSlug}'");
            }
        }

        // ----- drive lookup: this manifest, then stored, then the other manifests in the request ---

        var declaredDrives = new Dictionary<Guid, OwnedDrive>();
        var appDrives = snapshot.Drives.Values.Where(x => x.AppId == appId).ToList();

        DriveFacts? Facts(TargetDrive? drive)
        {
            if (drive?.Alias == null)
            {
                return null;
            }

            if (declaredDrives.TryGetValue(drive.Alias, out var declared) && declared.TargetDrive == drive)
            {
                return new DriveFacts(drive, declared.Name, appId, declared.OwnerOnly, declared.AllowAnonymousReads);
            }

            if (snapshot.Drives.TryGetValue(drive.Alias, out var stored) && stored.TargetDriveInfo == drive)
            {
                return new DriveFacts(drive, stored.Name, stored.AppId, stored.OwnerOnly, stored.AllowAnonymousReads);
            }

            foreach (var other in others)
            {
                var d = other.OwnedDrives?.FirstOrDefault(x => x?.TargetDrive == drive);
                if (d != null)
                {
                    return new DriveFacts(drive, d.Name, other.AppId, d.OwnerOnly, d.AllowAnonymousReads);
                }
            }

            return null;
        }

        // ----- owned drives --------------------------------------------------------------------

        var seenSlugs = new HashSet<string>(StringComparer.Ordinal);
        var ownedDrives = m.OwnedDrives ?? [];

        for (var i = 0; i < ownedDrives.Count; i++)
        {
            var subject = $"ownedDrives[{i}]";
            var d = ownedDrives[i];
            if (d?.TargetDrive == null || !d.TargetDrive.IsValid())
            {
                Problem(Codes.InvalidTargetDrive, subject, "A valid target drive (alias and type) is required");
                continue;
            }

            var problemsBefore = problems.Count;
            if (string.IsNullOrWhiteSpace(d.Name))
            {
                Problem(Codes.NameRequired, subject, "A drive name is required");
            }

            if (!OdinSlug.IsValid(d.DriveSlug))
            {
                Problem(Codes.InvalidSlug, $"{subject}.driveSlug", $"'{d.DriveSlug}' is not a valid slug");
            }

            if (!OdinSlug.IsValid(d.DriveTypeSlug))
            {
                Problem(Codes.InvalidSlug, $"{subject}.driveTypeSlug", $"'{d.DriveTypeSlug}' is not a valid slug");
            }

            if (d.OwnerOnly && (d.AllowAnonymousReads || d.AllowSubscriptions))
            {
                Problem(Codes.InvalidDriveFlags, subject, "An owner-only drive cannot allow anonymous reads or subscriptions");
            }

            if (declaredDrives.ContainsKey(d.TargetDrive.Alias))
            {
                Problem(Codes.DuplicateDrive, subject, "This drive is declared more than once");
                continue;
            }

            if (OdinSlug.IsValid(d.DriveSlug) && !seenSlugs.Add(d.DriveSlug))
            {
                Problem(Codes.DuplicateSlug, $"{subject}.driveSlug", $"The drive slug '{d.DriveSlug}' is declared more than once");
            }

            var otherClaim = others.FirstOrDefault(o => o.OwnedDrives?.Any(x => x?.TargetDrive?.Alias == d.TargetDrive.Alias) ?? false);
            if (otherClaim != null)
            {
                Problem(Codes.DriveOwnedElsewhere, subject, $"'{otherClaim.Name}' in this request also declares this drive");
            }

            declaredDrives[d.TargetDrive.Alias] = d;

            if (snapshot.Drives.TryGetValue(d.TargetDrive.Alias, out var stored))
            {
                if (stored.AppId != appId)
                {
                    Problem(Codes.DriveOwnedElsewhere, subject, stored.AppId == null
                        ? $"The drive '{stored.Name}' already exists and belongs to no app; adopt it from the drive page instead"
                        : $"The drive '{stored.Name}' already belongs to {snapshot.NameOf(stored.AppId.Value)}");
                }
                else if (!DriveMatches(stored, d))
                {
                    Problem(Codes.OwnedDriveDiffers, subject,
                        $"This app already owns '{stored.Name}' with different settings; update the drive instead");
                }
                else
                {
                    plan.Diff.DrivesAlreadyOwned.Add(d);
                }

                continue;
            }

            if (OdinSlug.IsValid(d.DriveSlug) && appDrives.Any(x => x.DriveSlug == d.DriveSlug))
            {
                Problem(Codes.DriveSlugTaken, $"{subject}.driveSlug", $"This app already has another drive with the slug '{d.DriveSlug}'");
            }

            if (problems.Count == problemsBefore)
            {
                plan.Diff.DrivesToCreate.Add(d);
            }
        }

        var ownedTargets = declaredDrives.Values.Select(d => d.TargetDrive).Concat(appDrives.Select(x => x.TargetDriveInfo)).ToHashSet();

        // ----- explicit grants -----------------------------------------------------------------

        List<DriveGrantRequest> explicitGrants;
        if (readsRegistration)
        {
            explicitGrants = m.Drives ?? [];
            for (var i = 0; i < explicitGrants.Count; i++)
            {
                var drive = explicitGrants[i]?.PermissionedDrive?.Drive;
                if (drive == null || !drive.IsValid())
                {
                    Problem(Codes.InvalidTargetDrive, $"drives[{i}]", "A valid target drive is required");
                }
                else if (Facts(drive) == null)
                {
                    Problem(Codes.DriveNotFound, $"drives[{i}]", $"No drive {drive} exists or is declared");
                }
            }
        }
        else
        {
            explicitGrants = existing == null ? [] : CurrentGrants(existing);
        }

        var explicitDrives = explicitGrants.Where(g => g?.PermissionedDrive?.Drive != null).Select(g => g.PermissionedDrive.Drive).ToHashSet();

        // ----- owned circles -------------------------------------------------------------------

        var declaredCircles = new HashSet<Guid>();
        var ownedCircles = m.OwnedCircles ?? [];
        for (var i = 0; i < ownedCircles.Count; i++)
        {
            var subject = $"ownedCircles[{i}]";
            var c = ownedCircles[i];
            if (c == null || c.Id == Guid.Empty)
            {
                Problem(Codes.CircleIdRequired, subject, "A circle id is required");
                continue;
            }

            if (SystemCircleConstants.IsSystemCircle(c.Id) || BuiltinApps.IsTreeDeclaredCircle(c.Id))
            {
                Problem(Codes.ReservedCircle, subject, "This is a built-in circle");
                continue;
            }

            if (!declaredCircles.Add(c.Id))
            {
                Problem(Codes.DuplicateCircle, subject, "This circle is declared more than once");
                continue;
            }

            var problemsBefore = problems.Count;
            if (string.IsNullOrWhiteSpace(c.Name))
            {
                Problem(Codes.NameRequired, subject, "A circle name is required");
            }

            var otherClaim = others.FirstOrDefault(o => o.OwnedCircles?.Any(x => x?.Id == c.Id) ?? false);
            if (otherClaim != null)
            {
                Problem(Codes.CircleOwnedElsewhere, subject, $"'{otherClaim.Name}' in this request also declares this circle");
            }

            var grants = c.DriveGrants ?? [];
            var keys = c.Permissions?.Keys ?? [];
            if (grants.Count == 0 && keys.Count == 0)
            {
                Problem(Codes.CircleGrantsNothing, subject, "A circle must grant at least one drive or one permission");
            }

            if (keys.Any(k => !PermissionKeyAllowance.IsValidCirclePermission(k)))
            {
                Problem(Codes.InvalidPermissionKey, $"{subject}.permissions", "A permission key is not allowed on circles");
            }

            var ambient = c.GrantOn is CircleGrantOn.Connect or CircleGrantOn.OwnFlowConnect;
            if (ambient && keys.Count > 0)
            {
                Problem(Codes.KeysOnAmbientCircle, $"{subject}.permissions", "A circle that enrols on connect cannot carry permission keys");
            }

            for (var j = 0; j < grants.Count; j++)
            {
                var grantSubject = $"{subject}.driveGrants[{j}]";
                var pd = grants[j]?.PermissionedDrive;
                if (pd?.Drive == null || !pd.Drive.IsValid())
                {
                    Problem(Codes.InvalidTargetDrive, grantSubject, "A valid target drive is required");
                    continue;
                }

                var facts = Facts(pd.Drive);
                if (facts == null)
                {
                    Problem(Codes.DriveNotFound, grantSubject, $"No drive {pd.Drive} exists or is declared");
                    continue;
                }

                // Confused-deputy rule: a circle may only hand out drives the app owns or is granted.
                if (!ownedTargets.Contains(pd.Drive) && !explicitDrives.Contains(pd.Drive))
                {
                    Problem(Codes.DriveNotGrantable, grantSubject, $"The circle grants '{facts.Name}', which this app neither owns nor is granted");
                }

                if (facts.OwnerOnly && !(pd.Permission.HasFlag(DrivePermission.Write) || pd.Permission.HasFlag(DrivePermission.React)))
                {
                    Problem(Codes.OwnerOnlyDrive, grantSubject, $"'{facts.Name}' is owner-only");
                }

                if (ambient && pd.Permission.HasFlag(DrivePermission.Read) && !facts.AllowAnonymousReads)
                {
                    Problem(Codes.ReadOnAmbientCircle, grantSubject, $"A circle that enrols on connect cannot grant read on '{facts.Name}'");
                }
            }

            // An update: the caller has checked the circle exists and is owned, and differing is the point.
            if (mode == ValidationMode.UpdateCircle)
            {
                continue;
            }

            if (!snapshot.Circles.TryGetValue(c.Id, out var storedCircle))
            {
                if (problems.Count == problemsBefore)
                {
                    plan.Diff.CirclesToCreate.Add(c);
                }
            }
            else if (storedCircle.AppId != appId)
            {
                Problem(Codes.CircleOwnedElsewhere, subject, storedCircle.AppId == null
                    ? $"The circle '{storedCircle.Name}' already exists and belongs to no app"
                    : $"The circle '{storedCircle.Name}' already belongs to {snapshot.NameOf(storedCircle.AppId.Value)}");
            }
            else if (!CircleMatches(storedCircle, c))
            {
                Problem(Codes.OwnedCircleDiffers, subject, $"This app already owns '{storedCircle.Name}' with different grants; update the circle instead");
            }
            else
            {
                plan.Diff.CirclesAlreadyOwned.Add(c);
            }
        }

        // ----- authorized circles --------------------------------------------------------------

        if (readsRegistration)
        {
            var authorized = m.AuthorizedCircles ?? [];
            for (var i = 0; i < authorized.Count; i++)
            {
                var id = authorized[i];
                var declaredSomewhere = declaredCircles.Contains(id) || others.Any(o => o.OwnedCircles?.Any(x => x?.Id == id) ?? false);
                if (id == Guid.Empty || (!snapshot.Circles.ContainsKey(id) && !declaredSomewhere))
                {
                    Problem(Codes.CircleNotFound, $"authorizedCircles[{i}]", $"Circle {id} does not exist or is declared");
                }
            }

            var memberGrant = m.CircleMemberPermissionGrant;
            var memberDrives = memberGrant?.Drives?.ToList() ?? [];
            for (var i = 0; i < memberDrives.Count; i++)
            {
                var drive = memberDrives[i]?.PermissionedDrive?.Drive;
                if (drive == null || !drive.IsValid() || Facts(drive) == null)
                {
                    Problem(Codes.DriveNotFound, $"circleMemberPermissionGrant.drives[{i}]", "No such drive exists or is declared");
                }
            }

            if (memberGrant?.PermissionSet?.Keys?.Any(k => !PermissionKeyAllowance.IsValidCirclePermission(k)) ?? false)
            {
                Problem(Codes.InvalidPermissionKey, "circleMemberPermissionGrant.permissionSet", "A permission key is not allowed on circles");
            }
        }

        if (mode == ValidationMode.UpdateCircle)
        {
            return plan;
        }

        // ----- the resulting access ------------------------------------------------------------

        var circleGrants = ownedCircles.Where(c => c != null && c.Id != Guid.Empty).SelectMany(c => c.DriveGrants ?? [])
            .Concat(snapshot.Circles.Values
                .Where(c => c.AppId == appId && !declaredCircles.Contains(c.Id.Value))
                .SelectMany(c => c.DriveGrants ?? []));

        plan.FinalGrants = BuildGrants(explicitGrants, ownedTargets, circleGrants);
        plan.PermissionSet = readsRegistration ? m.PermissionSet : existing?.Grant?.PermissionSet;
        plan.UpdatesAuthorizedCircles = readsRegistration;
        FillAccessDiff(plan, snapshot, Facts);

        return plan;
    }

    private static void ValidateAppFields(AppManifestV2 m, RedactedAppRegistration? existing, Action<string, string, string> problem)
    {
        if (string.IsNullOrWhiteSpace(m.Name))
        {
            problem(Codes.NameRequired, "name", "An app name is required");
        }
        else if (existing != null && existing.Name != m.Name)
        {
            problem(Codes.Immutable, "name", "The app name cannot be changed");
        }

        if (!OdinSlug.IsValid(m.AppSlug))
        {
            problem(Codes.InvalidSlug, "appSlug", $"'{m.AppSlug}' is not a valid app slug");
        }
        else if (existing != null && existing.AppSlug != m.AppSlug)
        {
            problem(Codes.Immutable, "appSlug", "The app slug cannot be changed");
        }

        var cors = string.IsNullOrWhiteSpace(m.CorsHostName) ? null : m.CorsHostName;
        if (cors != null && !AppUtil.IsValidCorsHeader(cors))
        {
            problem(Codes.InvalidCorsHostName, "corsHostName", "The CORS host must be [host name]:[port number]");
        }
        else if (existing != null && (existing.CorsHostName ?? "") != (cors ?? ""))
        {
            problem(Codes.Immutable, "corsHostName", "The CORS host cannot be changed");
        }
    }

    private static void FillAccessDiff(AppRegistrationPlan plan, Snapshot snapshot, Func<TargetDrive?, DriveFacts?> facts)
    {
        DriveAccessEntry Entry(PermissionedDrive pd)
        {
            var f = facts(pd.Drive);
            return new DriveAccessEntry
            {
                TargetDrive = pd.Drive,
                Permission = pd.Permission,
                DriveName = f?.Name,
                OwningAppId = f?.OwningAppId,
                OwningAppName = f?.OwningAppId == null ? null : snapshot.NameOf(f.OwningAppId.Value)
            };
        }

        var diff = plan.Diff;
        var existing = plan.Existing;
        var after = plan.FinalGrants.Select(g => g.PermissionedDrive).ToDictionary(pd => pd.Drive);

        // RegisterAppAsync and UpdateAppPermissionsAsync add the transient drive for transit; mirror it so
        // it is not reported as a change.
        var keysAfter = plan.PermissionSet?.Keys ?? [];
        if (keysAfter.Contains(PermissionKeys.UseTransitRead) || keysAfter.Contains(PermissionKeys.UseTransitWrite))
        {
            after.TryAdd(SystemDriveConstants.TransientTempDrive,
                new PermissionedDrive { Drive = SystemDriveConstants.TransientTempDrive, Permission = DrivePermission.ReadWrite });
        }

        var before = (existing?.Grant?.DriveGrants ?? [])
            .Select(g => g.PermissionedDrive)
            .GroupBy(pd => pd.Drive)
            .ToDictionary(grp => grp.Key, grp => grp.First());

        diff.DriveAccess.AddRange(after.Values.Select(Entry));
        diff.DriveAccessGained.AddRange(after
            .Where(kv => (kv.Value.Permission & ~(before.GetValueOrDefault(kv.Key)?.Permission ?? DrivePermission.None)) != 0)
            .Select(kv => Entry(kv.Value)));
        diff.DriveAccessLost.AddRange(before
            .Where(kv => (kv.Value.Permission & ~(after.GetValueOrDefault(kv.Key)?.Permission ?? DrivePermission.None)) != 0)
            .Select(kv => Entry(kv.Value)));

        var keysBefore = existing?.Grant?.PermissionSet?.Keys ?? [];
        diff.PermissionKeysGained.AddRange(keysAfter.Except(keysBefore));
        diff.PermissionKeysLost.AddRange(keysBefore.Except(keysAfter));

        var circlesAfter = plan.UpdatesAuthorizedCircles ? plan.Manifest.AuthorizedCircles ?? [] : existing?.AuthorizedCircles ?? [];
        var circlesBefore = existing?.AuthorizedCircles ?? [];
        diff.AuthorizedCirclesAdded.AddRange(circlesAfter.Except(circlesBefore));
        diff.AuthorizedCirclesRemoved.AddRange(circlesBefore.Except(circlesAfter));
    }

    // ============================================================================================
    // Helpers
    // ============================================================================================

    private async Task<RedactedAppRegistration> GetUpdatableAppAsync(Guid appId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        if (IsReserved(appId))
        {
            throw new OdinClientException("This is a built-in app; it is managed by the identity, not V2",
                OdinClientErrorCode.ArgumentError);
        }

        return await appRegistrationService.GetAppRegistration(appId, odinContext)
               ?? throw new OdinClientException("App is not registered", OdinClientErrorCode.AppNotRegistered);
    }

    private static T AssertOwnedBy<T>(T? entity, Func<T, Guid?> owningAppId, Guid appId, string what) where T : class
    {
        if (entity == null)
        {
            throw new OdinClientException($"{what} does not exist", OdinClientErrorCode.UnknownId);
        }

        if (owningAppId(entity) != appId)
        {
            throw new OdinClientException($"{what} is not owned by this app", OdinClientErrorCode.ArgumentError);
        }

        return entity;
    }

    private static DriveGrantRequest Grant(TargetDrive drive, DrivePermission permission) =>
        new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission } };

    private static List<DriveGrantRequest> CurrentGrants(RedactedAppRegistration app)
    {
        return (app.Grant?.DriveGrants ?? []).Select(g => new DriveGrantRequest { PermissionedDrive = g.PermissionedDrive }).ToList();
    }

    private static AppRegistrationV2 Compose(RedactedAppRegistration app, IEnumerable<StorageDrive> drives,
        IEnumerable<CircleDefinition> circles)
    {
        return new AppRegistrationV2
        {
            Registration = app,
            IsReserved = IsReserved(app.AppId.Value),
            OwnedDrives = drives.Select(d => new OwnedDriveInfo
            {
                DriveId = d.Id,
                TargetDrive = d.TargetDriveInfo,
                Name = d.Name,
                DriveSlug = d.DriveSlug,
                DriveTypeSlug = d.DriveTypeSlug,
                AllowAnonymousReads = d.AllowAnonymousReads,
                AllowSubscriptions = d.AllowSubscriptions,
                AllowCdn = d.IsCdnEnabled(),
                OwnerOnly = d.OwnerOnly,
                IsArchived = d.IsArchived
            }).ToList(),
            OwnedCircles = circles.Select(c => c.Redacted()).ToList()
        };
    }

    private static bool DriveMatches(StorageDrive stored, OwnedDrive declared)
    {
        return stored.TargetDriveInfo == declared.TargetDrive &&
               stored.DriveSlug == declared.DriveSlug &&
               stored.DriveTypeSlug == declared.DriveTypeSlug &&
               stored.AllowAnonymousReads == declared.AllowAnonymousReads &&
               stored.AllowSubscriptions == declared.AllowSubscriptions &&
               stored.OwnerOnly == declared.OwnerOnly;
    }

    private static bool CircleMatches(CircleDefinition stored, OwnedCircle declared)
    {
        return SameGrants(stored.DriveGrants, declared.DriveGrants) &&
               SameKeys(stored.Permissions, declared.Permissions) &&
               stored.GrantOn == declared.GrantOn &&
               stored.Designation == declared.Designation;
    }

    /// <summary>
    /// Same drives, permissions and temporal windows.  <c>DriveGrantRequest</c> equality compares only the
    /// drive; <c>PermissionedDrive</c>'s <c>==</c> compares all three.
    /// </summary>
    private static bool SameGrants(IEnumerable<DriveGrantRequest>? a, IEnumerable<DriveGrantRequest>? b)
    {
        var left = (a ?? []).Where(g => g?.PermissionedDrive != null).Select(g => g.PermissionedDrive).ToList();
        var right = (b ?? []).Where(g => g?.PermissionedDrive != null).Select(g => g.PermissionedDrive).ToList();
        return left.Count == right.Count && left.All(l => right.Any(r => r == l)) && right.All(r => left.Any(l => l == r));
    }

    private static bool SameKeys(PermissionSet? a, PermissionSet? b) => (a ?? new PermissionSet()) == (b ?? new PermissionSet());

    private static bool SameCircles(IEnumerable<Guid>? a, IEnumerable<Guid>? b) => (a ?? []).ToHashSet().SetEquals(b ?? []);

    private static bool SameMemberGrant(PermissionSetGrantRequest? a, PermissionSetGrantRequest? b) =>
        SameGrants(a?.Drives, b?.Drives) && SameKeys(a?.PermissionSet, b?.PermissionSet);
}

/// <summary>A validated manifest: its problems, what applying it changes, and the grants it results in.</summary>
internal sealed class AppRegistrationPlan
{
    public required AppManifestV2 Manifest { get; init; }
    public RedactedAppRegistration? Existing { get; init; }
    public Guid AppId => Manifest.AppId;
    public List<AppRegistrationProblem> Problems { get; } = [];
    public AppRegistrationDiff Diff { get; } = new();
    public List<DriveGrantRequest> FinalGrants { get; set; } = [];
    public PermissionSet? PermissionSet { get; set; }

    /// <summary>False when the manifest carried only owned resources (add-owned), so circles are left alone.</summary>
    public bool UpdatesAuthorizedCircles { get; set; }

    public AppRegistrationValidationResult ToResult() => new()
    {
        IsRegistered = Existing != null,
        Problems = Problems,
        Diff = Diff
    };
}
