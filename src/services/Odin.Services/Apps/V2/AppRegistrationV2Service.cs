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
/// All client-caused failures surface before the first write: <see cref="ValidateAsync"/> collects every
/// problem, and the write paths refuse to start unless it found none.  After that, nothing here is
/// transactional end to end (drive creation publishes notifications, circle updates rewrite member ICRs
/// one by one), so each write is safe to retry instead.
/// </para>
/// </remarks>
public class AppRegistrationV2Service(
    IAppRegistrationService appRegistrationService,
    IDriveManager driveManager,
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
        var drives = (await driveManager.GetDrivesAsync(PageOptions.All, odinContext)).Results;
        var circles = await circleDefinitionService.GetCirclesAsync(includeSystemCircle: false);

        return apps
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(app => Compose(app, drives, circles))
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
        var circles = await circleDefinitionService.GetCirclesAsync(includeSystemCircle: false);
        return Compose(app, drives, circles);
    }

    // ============================================================================================
    // Validate / register
    // ============================================================================================

    /// <summary>
    /// Every problem with the manifest, and what applying it would change.  Install wording when the app
    /// is not registered, update wording when it is.  Writes nothing.
    /// </summary>
    public async Task<AppRegistrationValidationResult> ValidateAsync(AppManifestV2 manifest, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(manifest, nameof(manifest));

        var outcome = await ValidateCoreAsync(manifest, ValidationMode.InstallOrUpdate, odinContext);
        return outcome.ToResult();
    }

    public async Task<AppRegistrationV2> RegisterAsync(AppManifestV2 manifest, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(manifest, nameof(manifest));

        var outcome = await ValidateCoreAsync(manifest, ValidationMode.Install, odinContext);
        outcome.ThrowIfInvalid();

        var appId = manifest.AppId;
        await CreateOwnedDrivesAsync(appId, outcome.DrivesToCreate, odinContext);
        await CreateOwnedCirclesAsync(appId, outcome.CirclesToCreate, odinContext);

        await appRegistrationService.RegisterAppAsync(new AppRegistrationRequest
        {
            AppId = appId,
            Name = manifest.Name,
            AppSlug = manifest.AppSlug,
            CorsHostName = string.IsNullOrWhiteSpace(manifest.CorsHostName) ? null! : manifest.CorsHostName,
            PermissionSet = manifest.PermissionSet ?? new PermissionSet(),
            Drives = outcome.FinalGrants,
            AuthorizedCircles = manifest.AuthorizedCircles ?? [],
            CircleMemberPermissionGrant = manifest.CircleMemberPermissionGrant ?? new PermissionSetGrantRequest()
        }, odinContext);

        return (await GetAsync(appId, odinContext))!;
    }

    // ============================================================================================
    // Updates
    // ============================================================================================

    /// <summary>
    /// Creates owned drives and circles for an installed app, in one call, then re-grants the app.
    /// </summary>
    public async Task<AppRegistrationV2> AddOwnedAsync(Guid appId, AddOwnedResourcesRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        var app = await GetUpdatableAppAsync(appId, odinContext);

        var manifest = new AppManifestV2
        {
            AppId = appId,
            Name = app.Name,
            AppSlug = app.AppSlug,
            CorsHostName = app.CorsHostName,
            OwnedDrives = request.OwnedDrives,
            OwnedCircles = request.OwnedCircles
        };

        var outcome = await ValidateCoreAsync(manifest, ValidationMode.AddOwned, odinContext);
        outcome.ThrowIfInvalid();

        await CreateOwnedDrivesAsync(appId, outcome.DrivesToCreate, odinContext);
        await CreateOwnedCirclesAsync(appId, outcome.CirclesToCreate, odinContext);
        await RebuildGrantsAsync(app, CurrentGrants(app), app.Grant?.PermissionSet, odinContext);

        return (await GetAsync(appId, odinContext))!;
    }

    /// <summary>
    /// Replaces the app's permission keys and explicit drive grants.  Owned drives and the drives of
    /// owned circles are merged back in, so the app keeps them unless an explicit grant says otherwise.
    /// </summary>
    public async Task UpdatePermissionsAsync(Guid appId, UpdateAppPermissionsV2Request request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
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
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        var app = await GetUpdatableAppAsync(appId, odinContext);

        var circles = request.AuthorizedCircles ?? [];
        foreach (var circleId in circles)
        {
            if (circleId == Guid.Empty || await circleDefinitionService.GetCircleAsync(circleId) == null)
            {
                throw new OdinClientException($"Circle {circleId} does not exist", OdinClientErrorCode.CircleNotFound);
            }
        }

        var grant = request.CircleMemberPermissionGrant ?? new PermissionSetGrantRequest();
        if (SameCircles(app.AuthorizedCircles, circles) && SameMemberGrant(app.CircleMemberPermissionSetGrantRequest, grant))
        {
            return;
        }

        await appRegistrationService.UpdateAuthorizedCirclesAsync(new UpdateAuthorizedCirclesRequest
        {
            AppId = appId,
            AuthorizedCircles = circles,
            CircleMemberPermissionGrant = grant
        }, odinContext);
    }

    public async Task UpdateOwnedDriveAsync(Guid appId, Guid driveId, UpdateOwnedDriveRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        await GetUpdatableAppAsync(appId, odinContext);

        var drive = await driveManager.GetDriveAsync(driveId);
        AssertOwnedBy(drive?.AppId, appId, drive == null, $"Drive {driveId}");

        // Checked up front so no setter refuses after another has already written.
        if (drive!.OwnerOnly && (request.AllowAnonymousReads == true || request.AllowSubscriptions == true))
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
            // Not on IDriveManager; DriveManager is the only implementation.
            if (driveManager is not DriveManager concrete)
            {
                throw new OdinSystemException("Archiving needs DriveManager");
            }

            await concrete.SetArchiveDriveFlagAsync(driveId, archived, odinContext);
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
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(circle, nameof(circle));
        var app = await GetUpdatableAppAsync(appId, odinContext);

        if (circle.Id != Guid.Empty && circle.Id != circleId)
        {
            throw new OdinClientException("The circle id in the body does not match the route", OdinClientErrorCode.ArgumentError);
        }

        var existing = await circleDefinitionService.GetCircleAsync(circleId);
        AssertOwnedBy(existing?.AppId, appId, existing == null, $"Circle {circleId}");

        // Validate fully before anything is written.  UpdateCircleDefinitionAsync rewrites member ICRs
        // before its own permission and deposit-only checks run.
        var manifest = new AppManifestV2
        {
            AppId = appId,
            Name = app.Name,
            AppSlug = app.AppSlug,
            OwnedCircles = [new OwnedCircle
            {
                Id = circleId,
                Name = circle.Name,
                Description = circle.Description,
                DriveGrants = circle.DriveGrants,
                Permissions = circle.Permissions,
                GrantOn = circle.GrantOn,
                Designation = circle.Designation,
                Emoji = circle.Emoji
            }]
        };

        var outcome = await ValidateCoreAsync(manifest, ValidationMode.UpdateCircle, odinContext);
        outcome.ThrowIfInvalid();

        var updated = new CircleDefinition
        {
            Id = existing!.Id,
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

        var grantsChanged = !SameGrants(existing.DriveGrants, updated.DriveGrants) ||
                            !SameKeys(existing.Permissions, updated.Permissions);

        if (grantsChanged)
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
        odinContext.Caller.AssertHasMasterKey();
        var app = await GetUpdatableAppAsync(appId, odinContext);

        var existing = await circleDefinitionService.GetCircleAsync(circleId);
        AssertOwnedBy(existing?.AppId, appId, existing == null, $"Circle {circleId}");

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
    private async Task CreateOwnedDrivesAsync(Guid appId, List<OwnedDrive> drives, IOdinContext odinContext)
    {
        foreach (var d in drives)
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

        var anonymous = drives.Where(d => d.AllowAnonymousReads).ToList();
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
            var added = false;
            foreach (var d in anonymous.Where(d => grants.All(g => g.PermissionedDrive.Drive != d.TargetDrive)))
            {
                grants.Add(new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive { Drive = d.TargetDrive, Permission = DrivePermission.Read }
                });
                added = true;
            }

            if (added)
            {
                def.DriveGrants = grants;
                await circleNetworkService.UpdateCircleDefinitionAsync(def, odinContext);
            }
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
    /// The grant rebuild: explicit grants, plus ReadWrite on owned drives, plus whatever the app's owned
    /// circles grant -- applied through the V1 method, which also adds the transient drive and resets the
    /// permission cache.
    /// </summary>
    /// <remarks>
    /// Owned circles are included because adopting or reassigning a circle adds its drives to the owning
    /// app's grant (<c>CircleNetworkService.GrantAppTheCirclesDrivesAsync</c>); rebuilding without them
    /// would silently take those away.
    /// </remarks>
    private async Task RebuildGrantsAsync(RedactedAppRegistration app, IEnumerable<DriveGrantRequest> explicitGrants,
        PermissionSet? permissionSet, IOdinContext odinContext)
    {
        var appId = app.AppId.Value;
        var ownedDrives = await driveManager.GetDrivesByAppIdAsync(appId, odinContext);
        var ownedCircles = (await circleDefinitionService.GetCirclesAsync(includeSystemCircle: false))
            .Where(c => c.AppId == appId);

        var grants = BuildGrants(
            explicitGrants,
            ownedDrives.Select(d => d.TargetDriveInfo),
            ownedCircles.SelectMany(c => c.DriveGrants ?? []));

        await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
        {
            AppId = appId,
            PermissionSet = permissionSet ?? new PermissionSet(),
            Drives = grants
        }, odinContext);
    }

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
            var drive = g.PermissionedDrive.Drive;
            if (merged.TryGetValue(drive, out var current))
            {
                current.Permission |= g.PermissionedDrive.Permission;
            }
            else
            {
                merged[drive] = new PermissionedDrive { Drive = drive, Permission = g.PermissionedDrive.Permission };
            }
        }

        return merged.Values.Select(pd => new DriveGrantRequest { PermissionedDrive = pd }).ToList();
    }

    // ============================================================================================
    // Validation
    // ============================================================================================

    private enum ValidationMode
    {
        /// <summary>Install when the app is not registered, update when it is.</summary>
        InstallOrUpdate,

        /// <summary>A registration; the app must not be registered.</summary>
        Install,

        /// <summary>Owned drives and circles for a registered app; nothing else in the manifest is read.</summary>
        AddOwned,

        /// <summary>A change to one owned circle, which must already exist.</summary>
        UpdateCircle
    }

    private sealed class DriveFacts
    {
        public required TargetDrive TargetDrive { get; init; }
        public required string Name { get; init; }
        public Guid? OwningAppId { get; init; }
        public bool OwnerOnly { get; init; }
        public bool AllowAnonymousReads { get; init; }
    }

    private sealed class ValidationOutcome
    {
        public bool IsRegistered { get; init; }
        public List<AppRegistrationProblem> Problems { get; } = [];
        public List<OwnedDrive> DrivesToCreate { get; } = [];
        public List<OwnedDrive> DrivesAlreadyOwned { get; } = [];
        public List<OwnedCircle> CirclesToCreate { get; } = [];
        public List<OwnedCircle> CirclesAlreadyOwned { get; } = [];
        public List<DriveGrantRequest> FinalGrants { get; set; } = [];
        public AppRegistrationDiff Diff { get; set; } = new();

        public void Problem(string code, string subject, string message)
        {
            Problems.Add(new AppRegistrationProblem { Code = code, Subject = subject, Message = message });
        }

        public void ThrowIfInvalid()
        {
            if (Problems.Count == 0)
            {
                return;
            }

            var message = string.Join("; ", Problems.Select(p =>
                string.IsNullOrEmpty(p.Subject) ? p.Message : $"{p.Subject}: {p.Message}"));

            var code = Problems.Any(p => p.Code == "alreadyRegistered")
                ? OdinClientErrorCode.IdAlreadyExists
                : OdinClientErrorCode.ArgumentError;

            throw new OdinClientException(message, code);
        }

        public AppRegistrationValidationResult ToResult() => new()
        {
            IsRegistered = IsRegistered,
            Problems = Problems,
            Diff = Diff
        };
    }

    private async Task<ValidationOutcome> ValidateCoreAsync(AppManifestV2 m, ValidationMode mode, IOdinContext odinContext)
    {
        var appId = m.AppId;
        var existing = appId == Guid.Empty ? null : await appRegistrationService.GetAppRegistration(appId, odinContext);
        var outcome = new ValidationOutcome { IsRegistered = existing != null };

        // ----- the app -------------------------------------------------------------------------

        if (appId == Guid.Empty)
        {
            outcome.Problem("appIdRequired", "appId", "An app id is required");
        }
        else if (IsReserved(appId))
        {
            outcome.Problem("reservedApp", "appId", "This is a built-in app; it is managed by the identity, not V2");
        }

        if (await legacyStore.IsPreMoveAsync())
        {
            outcome.Problem("identityNotUpgraded", "", "This identity has not finished upgrading; try again shortly");
        }

        if (mode == ValidationMode.Install && existing != null)
        {
            outcome.Problem("alreadyRegistered", "appId", "This app is already registered");
        }

        var readsRegistration = mode is ValidationMode.Install or ValidationMode.InstallOrUpdate;
        if (readsRegistration)
        {
            ValidateAppFields(m, existing, outcome);
            if (existing == null && OdinSlug.IsValid(m.AppSlug))
            {
                var holder = await appRegistrationService.GetAppRegistrationBySlugAsync(m.AppSlug, odinContext);
                if (holder != null && holder.AppId.Value != appId)
                {
                    outcome.Problem("slugTaken", "appSlug", $"Another app already holds the slug '{m.AppSlug}'");
                }
            }
        }

        // ----- what exists ---------------------------------------------------------------------

        var allDrives = (await driveManager.GetDrivesAsync(PageOptions.All, odinContext)).Results
            .ToDictionary(d => d.Id);
        var allCircles = (await circleDefinitionService.GetCirclesAsync(includeSystemCircle: true))
            .ToDictionary(c => c.Id.Value);
        var appNames = (await appRegistrationService.GetRegisteredAppsAsync(odinContext))
            .ToDictionary(a => a.AppId.Value, a => a.Name);

        var declaredDrives = new Dictionary<Guid, OwnedDrive>();

        DriveFacts? Facts(TargetDrive? drive)
        {
            if (drive?.Alias == null)
            {
                return null;
            }

            if (declaredDrives.TryGetValue(drive.Alias, out var declared) && declared.TargetDrive == drive)
            {
                return new DriveFacts
                {
                    TargetDrive = drive, Name = declared.Name, OwningAppId = appId,
                    OwnerOnly = declared.OwnerOnly, AllowAnonymousReads = declared.AllowAnonymousReads
                };
            }

            if (allDrives.TryGetValue(drive.Alias, out var stored) && stored.TargetDriveInfo == drive)
            {
                return new DriveFacts
                {
                    TargetDrive = drive, Name = stored.Name, OwningAppId = stored.AppId,
                    OwnerOnly = stored.OwnerOnly, AllowAnonymousReads = stored.AllowAnonymousReads
                };
            }

            return null;
        }

        // ----- owned drives --------------------------------------------------------------------

        var seenAliases = new HashSet<Guid>();
        var seenSlugs = new HashSet<string>(StringComparer.Ordinal);
        var ownedDrives = m.OwnedDrives ?? [];

        for (var i = 0; i < ownedDrives.Count; i++)
        {
            var subject = $"ownedDrives[{i}]";
            var d = ownedDrives[i];
            if (d?.TargetDrive == null || !d.TargetDrive.IsValid())
            {
                outcome.Problem("invalidTargetDrive", subject, "A valid target drive (alias and type) is required");
                continue;
            }

            var valid = true;
            if (string.IsNullOrWhiteSpace(d.Name))
            {
                outcome.Problem("nameRequired", subject, "A drive name is required");
                valid = false;
            }

            if (!OdinSlug.IsValid(d.DriveSlug))
            {
                outcome.Problem("invalidSlug", $"{subject}.driveSlug", $"'{d.DriveSlug}' is not a valid slug");
                valid = false;
            }

            if (!OdinSlug.IsValid(d.DriveTypeSlug))
            {
                outcome.Problem("invalidSlug", $"{subject}.driveTypeSlug", $"'{d.DriveTypeSlug}' is not a valid slug");
                valid = false;
            }

            if (d.OwnerOnly && (d.AllowAnonymousReads || d.AllowSubscriptions))
            {
                outcome.Problem("invalidDriveFlags", subject,
                    "An owner-only drive cannot allow anonymous reads or subscriptions");
                valid = false;
            }

            if (!seenAliases.Add(d.TargetDrive.Alias))
            {
                outcome.Problem("duplicateDrive", subject, "This drive is declared more than once");
                continue;
            }

            if (OdinSlug.IsValid(d.DriveSlug) && !seenSlugs.Add(d.DriveSlug))
            {
                outcome.Problem("duplicateSlug", $"{subject}.driveSlug", $"The drive slug '{d.DriveSlug}' is declared more than once");
                valid = false;
            }

            if (allDrives.TryGetValue(d.TargetDrive.Alias, out var stored))
            {
                if (stored.AppId != appId)
                {
                    outcome.Problem("driveOwnedElsewhere", subject, stored.AppId == null
                        ? $"The drive '{stored.Name}' already exists and belongs to no app; adopt it from the drive page instead"
                        : $"The drive '{stored.Name}' already belongs to {NameOf(stored.AppId.Value)}");
                }
                else if (!DriveMatches(stored, d))
                {
                    outcome.Problem("ownedDriveDiffers", subject,
                        $"This app already owns '{stored.Name}' with different settings; update the drive instead");
                }
                else
                {
                    outcome.DrivesAlreadyOwned.Add(d);
                    declaredDrives[d.TargetDrive.Alias] = d;
                }

                continue;
            }

            if (valid && allDrives.Values.Any(x => x.AppId == appId && x.DriveSlug == d.DriveSlug))
            {
                outcome.Problem("driveSlugTaken", $"{subject}.driveSlug",
                    $"This app already has another drive with the slug '{d.DriveSlug}'");
                valid = false;
            }

            declaredDrives[d.TargetDrive.Alias] = d;
            if (valid)
            {
                outcome.DrivesToCreate.Add(d);
            }
        }

        bool IsOwned(TargetDrive drive) =>
            (declaredDrives.TryGetValue(drive.Alias, out var dd) && dd.TargetDrive == drive) ||
            allDrives.Values.Any(x => x.AppId == appId && x.TargetDriveInfo == drive);

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
                    outcome.Problem("invalidTargetDrive", $"drives[{i}]", "A valid target drive is required");
                }
                else if (Facts(drive) == null)
                {
                    outcome.Problem("driveNotFound", $"drives[{i}]", $"No drive {drive} exists or is declared");
                }
            }
        }
        else
        {
            explicitGrants = existing == null ? [] : CurrentGrants(existing);
        }

        var explicitDrives = explicitGrants
            .Where(g => g?.PermissionedDrive?.Drive != null)
            .Select(g => g.PermissionedDrive.Drive)
            .ToHashSet();

        // ----- owned circles -------------------------------------------------------------------

        var seenCircles = new HashSet<Guid>();
        var ownedCircles = m.OwnedCircles ?? [];
        for (var i = 0; i < ownedCircles.Count; i++)
        {
            var subject = $"ownedCircles[{i}]";
            var c = ownedCircles[i];
            if (c == null || c.Id == Guid.Empty)
            {
                outcome.Problem("circleIdRequired", subject, "A circle id is required");
                continue;
            }

            var valid = true;
            if (SystemCircleConstants.IsSystemCircle(c.Id) || BuiltinApps.IsTreeDeclaredCircle(c.Id))
            {
                outcome.Problem("reservedCircle", subject, "This is a built-in circle");
                continue;
            }

            if (!seenCircles.Add(c.Id))
            {
                outcome.Problem("duplicateCircle", subject, "This circle is declared more than once");
                continue;
            }

            if (string.IsNullOrWhiteSpace(c.Name))
            {
                outcome.Problem("nameRequired", subject, "A circle name is required");
                valid = false;
            }

            var grants = c.DriveGrants ?? [];
            var keys = c.Permissions?.Keys ?? [];
            if (grants.Count == 0 && keys.Count == 0)
            {
                outcome.Problem("circleGrantsNothing", subject, "A circle must grant at least one drive or one permission");
                valid = false;
            }

            if (keys.Any(k => !PermissionKeyAllowance.IsValidCirclePermission(k)))
            {
                outcome.Problem("invalidPermissionKey", $"{subject}.permissions", "A permission key is not allowed on circles");
                valid = false;
            }

            var ambient = c.GrantOn is CircleGrantOn.Connect or CircleGrantOn.OwnFlowConnect;
            if (ambient && keys.Count > 0)
            {
                outcome.Problem("keysOnAmbientCircle", $"{subject}.permissions",
                    "A circle that enrols on connect cannot carry permission keys");
                valid = false;
            }

            for (var j = 0; j < grants.Count; j++)
            {
                var grantSubject = $"{subject}.driveGrants[{j}]";
                var pd = grants[j]?.PermissionedDrive;
                if (pd?.Drive == null || !pd.Drive.IsValid())
                {
                    outcome.Problem("invalidTargetDrive", grantSubject, "A valid target drive is required");
                    valid = false;
                    continue;
                }

                var facts = Facts(pd.Drive);
                if (facts == null)
                {
                    outcome.Problem("driveNotFound", grantSubject, $"No drive {pd.Drive} exists or is declared");
                    valid = false;
                    continue;
                }

                // Confused-deputy rule: a circle may only hand out drives the app owns or is granted.
                if (!IsOwned(pd.Drive) && !explicitDrives.Contains(pd.Drive))
                {
                    outcome.Problem("driveNotGrantable", grantSubject,
                        $"The circle grants '{facts.Name}', which this app neither owns nor is granted");
                    valid = false;
                }

                if (facts.OwnerOnly && !(pd.Permission.HasFlag(DrivePermission.Write) || pd.Permission.HasFlag(DrivePermission.React)))
                {
                    outcome.Problem("ownerOnlyDrive", grantSubject, $"'{facts.Name}' is owner-only");
                    valid = false;
                }

                if (ambient && pd.Permission.HasFlag(DrivePermission.Read) && !facts.AllowAnonymousReads)
                {
                    outcome.Problem("readOnAmbientCircle", grantSubject,
                        $"A circle that enrols on connect cannot grant read on '{facts.Name}'");
                    valid = false;
                }
            }

            if (!allCircles.TryGetValue(c.Id, out var stored))
            {
                if (mode == ValidationMode.UpdateCircle)
                {
                    outcome.Problem("circleNotFound", subject, "The circle does not exist");
                }
                else if (valid)
                {
                    outcome.CirclesToCreate.Add(c);
                }

                continue;
            }

            if (stored.AppId != appId)
            {
                outcome.Problem("circleOwnedElsewhere", subject, stored.AppId == null
                    ? $"The circle '{stored.Name}' already exists and belongs to no app"
                    : $"The circle '{stored.Name}' already belongs to {NameOf(stored.AppId.Value)}");
            }
            else if (mode == ValidationMode.UpdateCircle)
            {
                // An update: differing from what is stored is the point.
            }
            else if (!CircleMatches(stored, c))
            {
                outcome.Problem("ownedCircleDiffers", subject,
                    $"This app already owns '{stored.Name}' with different grants; update the circle instead");
            }
            else
            {
                outcome.CirclesAlreadyOwned.Add(c);
            }
        }

        // ----- authorized circles --------------------------------------------------------------

        if (readsRegistration)
        {
            var authorized = m.AuthorizedCircles ?? [];
            for (var i = 0; i < authorized.Count; i++)
            {
                var id = authorized[i];
                if (id == Guid.Empty || (!allCircles.ContainsKey(id) && !seenCircles.Contains(id)))
                {
                    outcome.Problem("circleNotFound", $"authorizedCircles[{i}]", $"Circle {id} does not exist or is declared");
                }
            }

            var memberGrant = m.CircleMemberPermissionGrant;
            var memberDrives = memberGrant?.Drives?.ToList() ?? [];
            for (var i = 0; i < memberDrives.Count; i++)
            {
                var drive = memberDrives[i]?.PermissionedDrive?.Drive;
                if (drive == null || !drive.IsValid() || Facts(drive) == null)
                {
                    outcome.Problem("driveNotFound", $"circleMemberPermissionGrant.drives[{i}]",
                        "No such drive exists or is declared");
                }
            }

            if (memberGrant?.PermissionSet?.Keys?.Any(k => !PermissionKeyAllowance.IsValidCirclePermission(k)) ?? false)
            {
                outcome.Problem("invalidPermissionKey", "circleMemberPermissionGrant.permissionSet",
                    "A permission key is not allowed on circles");
            }
        }

        // ----- the resulting access ------------------------------------------------------------

        var ownedTargets = declaredDrives.Values.Select(d => d.TargetDrive)
            .Concat(allDrives.Values.Where(x => x.AppId == appId).Select(x => x.TargetDriveInfo))
            .Distinct()
            .ToList();

        var circleGrants = ownedCircles.Where(c => c != null && c.Id != Guid.Empty).SelectMany(c => c.DriveGrants ?? [])
            .Concat(allCircles.Values
                .Where(c => c.AppId == appId && ownedCircles.All(oc => oc?.Id != c.Id.Value))
                .SelectMany(c => c.DriveGrants ?? []));

        outcome.FinalGrants = BuildGrants(explicitGrants, ownedTargets, circleGrants);

        if (mode != ValidationMode.UpdateCircle)
        {
            outcome.Diff = BuildDiff(m, existing, outcome, Facts, NameOf, readsRegistration);
        }

        return outcome;

        string NameOf(Guid id) => id == appId ? (string.IsNullOrWhiteSpace(m.Name) ? "this app" : m.Name)
            : appNames.TryGetValue(id, out var n) ? n : $"app {id}";
    }

    private static void ValidateAppFields(AppManifestV2 m, RedactedAppRegistration? existing, ValidationOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(m.Name))
        {
            outcome.Problem("nameRequired", "name", "An app name is required");
        }
        else if (existing != null && existing.Name != m.Name)
        {
            outcome.Problem("immutable", "name", "The app name cannot be changed");
        }

        if (!OdinSlug.IsValid(m.AppSlug))
        {
            outcome.Problem("invalidSlug", "appSlug", $"'{m.AppSlug}' is not a valid app slug");
        }
        else if (existing != null && existing.AppSlug != m.AppSlug)
        {
            outcome.Problem("immutable", "appSlug", "The app slug cannot be changed");
        }

        var cors = string.IsNullOrWhiteSpace(m.CorsHostName) ? null : m.CorsHostName;
        if (cors != null && !AppUtil.IsValidCorsHeader(cors))
        {
            outcome.Problem("invalidCorsHostName", "corsHostName", "The CORS host must be [host name]:[port number]");
        }
        else if (existing != null && (existing.CorsHostName ?? "") != (cors ?? ""))
        {
            outcome.Problem("immutable", "corsHostName", "The CORS host cannot be changed");
        }
    }

    private static AppRegistrationDiff BuildDiff(
        AppManifestV2 m,
        RedactedAppRegistration? existing,
        ValidationOutcome outcome,
        Func<TargetDrive?, DriveFacts?> facts,
        Func<Guid, string> nameOf,
        bool readsRegistration)
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
                OwningAppName = f?.OwningAppId == null ? null : nameOf(f.OwningAppId.Value)
            };
        }

        var after = outcome.FinalGrants.Select(g => g.PermissionedDrive).ToDictionary(pd => pd.Drive);

        // RegisterAppAsync and UpdateAppPermissionsAsync add the transient drive for transit; mirror it so
        // it is not reported as a change.
        var keysAfter = readsRegistration ? m.PermissionSet?.Keys ?? [] : existing?.Grant?.PermissionSet?.Keys ?? [];
        if (keysAfter.Contains(PermissionKeys.UseTransitRead) || keysAfter.Contains(PermissionKeys.UseTransitWrite))
        {
            after.TryAdd(SystemDriveConstants.TransientTempDrive,
                new PermissionedDrive { Drive = SystemDriveConstants.TransientTempDrive, Permission = DrivePermission.ReadWrite });
        }

        var before = (existing?.Grant?.DriveGrants ?? [])
            .Select(g => g.PermissionedDrive)
            .GroupBy(pd => pd.Drive)
            .ToDictionary(grp => grp.Key, grp => grp.First());

        var gained = new List<DriveAccessEntry>();
        var lost = new List<DriveAccessEntry>();

        foreach (var (drive, pd) in after)
        {
            var old = before.TryGetValue(drive, out var b) ? b.Permission : DrivePermission.None;
            if ((pd.Permission & ~old) != 0)
            {
                gained.Add(Entry(pd));
            }
        }

        foreach (var (drive, pd) in before)
        {
            var now = after.TryGetValue(drive, out var a) ? a.Permission : DrivePermission.None;
            if ((pd.Permission & ~now) != 0)
            {
                lost.Add(Entry(pd));
            }
        }

        var keysBefore = existing?.Grant?.PermissionSet?.Keys ?? [];
        var circlesAfter = readsRegistration ? m.AuthorizedCircles ?? [] : existing?.AuthorizedCircles ?? [];
        var circlesBefore = existing?.AuthorizedCircles ?? [];

        return new AppRegistrationDiff
        {
            DrivesToCreate = outcome.DrivesToCreate,
            DrivesAlreadyOwned = outcome.DrivesAlreadyOwned,
            CirclesToCreate = outcome.CirclesToCreate,
            CirclesAlreadyOwned = outcome.CirclesAlreadyOwned,
            DriveAccess = after.Values.Select(Entry).ToList(),
            DriveAccessGained = gained,
            DriveAccessLost = lost,
            PermissionKeysGained = keysAfter.Except(keysBefore).ToList(),
            PermissionKeysLost = keysBefore.Except(keysAfter).ToList(),
            AuthorizedCirclesAdded = circlesAfter.Except(circlesBefore).ToList(),
            AuthorizedCirclesRemoved = circlesBefore.Except(circlesAfter).ToList()
        };
    }

    // ============================================================================================
    // Helpers
    // ============================================================================================

    private async Task<RedactedAppRegistration> GetUpdatableAppAsync(Guid appId, IOdinContext odinContext)
    {
        if (IsReserved(appId))
        {
            throw new OdinClientException("This is a built-in app; it is managed by the identity, not V2",
                OdinClientErrorCode.ArgumentError);
        }

        var app = await appRegistrationService.GetAppRegistration(appId, odinContext);
        if (app == null)
        {
            throw new OdinClientException("App is not registered", OdinClientErrorCode.AppNotRegistered);
        }

        return app;
    }

    private static void AssertOwnedBy(Guid? owningAppId, Guid appId, bool missing, string what)
    {
        if (missing)
        {
            throw new OdinClientException($"{what} does not exist", OdinClientErrorCode.UnknownId);
        }

        if (owningAppId != appId)
        {
            throw new OdinClientException($"{what} is not owned by this app", OdinClientErrorCode.ArgumentError);
        }
    }

    private static List<DriveGrantRequest> CurrentGrants(RedactedAppRegistration app)
    {
        return (app.Grant?.DriveGrants ?? [])
            .Select(g => new DriveGrantRequest { PermissionedDrive = g.PermissionedDrive })
            .ToList();
    }

    private static AppRegistrationV2 Compose(RedactedAppRegistration app, IEnumerable<StorageDrive> drives,
        IEnumerable<CircleDefinition> circles)
    {
        var appId = app.AppId.Value;
        return new AppRegistrationV2
        {
            Registration = app,
            IsReserved = IsReserved(appId),
            OwnedDrives = drives.Where(d => d.AppId == appId).Select(d => new OwnedDriveInfo
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
            OwnedCircles = circles.Where(c => c.AppId == appId).Select(c => c.Redacted()).ToList()
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
    /// Drive and permission both -- <c>DriveGrantRequest</c> equality compares only the drive.
    /// </summary>
    private static bool SameGrants(IEnumerable<DriveGrantRequest>? a, IEnumerable<DriveGrantRequest>? b)
    {
        static HashSet<(Guid, Guid, DrivePermission)> Set(IEnumerable<DriveGrantRequest>? grants) =>
            (grants ?? []).Where(g => g?.PermissionedDrive?.Drive != null)
            .Select(g => (g.PermissionedDrive.Drive.Alias.Value, g.PermissionedDrive.Drive.Type.Value, g.PermissionedDrive.Permission))
            .ToHashSet();

        return Set(a).SetEquals(Set(b));
    }

    private static bool SameKeys(PermissionSet? a, PermissionSet? b)
    {
        return (a?.Keys ?? []).ToHashSet().SetEquals(b?.Keys ?? []);
    }

    private static bool SameCircles(IEnumerable<Guid>? a, IEnumerable<Guid>? b)
    {
        return (a ?? []).ToHashSet().SetEquals(b ?? []);
    }

    private static bool SameMemberGrant(PermissionSetGrantRequest? a, PermissionSetGrantRequest? b)
    {
        return SameGrants(a?.Drives, b?.Drives) && SameKeys(a?.PermissionSet, b?.PermissionSet);
    }
}
