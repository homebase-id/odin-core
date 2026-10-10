using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
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
/// -- <c>VersionUpgradeService</c> does a single up-front pass so migrations can assume every drive
/// exists rather than each creating what it needs.  Everything here is idempotent for that reason.
/// </para>
/// </remarks>
public class BuiltinProvisioner(
    ILogger<BuiltinProvisioner> logger,
    IDriveManager driveManager,
    CircleDefinitionService circleDefinitionService,
    IAppRegistrationService appRegistrationService)
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

    /// <summary>
    /// Creates the drives of every built-in app, plus the carry-overs.  Idempotent.
    /// </summary>
    public async Task EnsureDrivesAsync(IOdinContext odinContext)
    {
        // Non-anonymous first -- see EnsureAllAsync for why.
        var drives = BuiltinApps.SeededDrives
            .Concat(CarryOverDrives)
            .DistinctBy(d => d.TargetDrive.Alias.Value)
            .OrderBy(d => d.AllowAnonymousReads)
            .ToList();

        foreach (var request in drives)
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
    /// Creates the circles owned by every built-in app.  Idempotent.
    /// </summary>
    public async Task EnsureCirclesAsync(IOdinContext odinContext)
    {
        foreach (var def in BuiltinApps.SeededCircles)
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

        if (!OdinSlug.IsValid(request.DriveSlug) || !OdinSlug.IsValid(request.DriveTypeSlug))
        {
            throw new OdinSystemException(
                $"Drive '{request.Name}' has no valid slug ('{request.DriveSlug}' / " +
                $"'{request.DriveTypeSlug}'); it cannot be provisioned");
        }
    }

    /// <summary>
    /// The registration constants carry no slug, so registering one as-is derives the slug from the
    /// display name.  The tree is the authority, so it is applied here.
    /// </summary>
    /// <remarks>
    /// Copied rather than assigned: the constants are shared statics, and provisioning runs per tenant.
    /// </remarks>
    private static AppRegistrationRequest WithSlug(AppRegistrationRequest request, string appSlug) => new()
    {
        AppId = request.AppId,
        Name = request.Name,
        AppSlug = appSlug,
        CorsHostName = request.CorsHostName,
        PermissionSet = request.PermissionSet,
        Drives = request.Drives,
        AuthorizedCircles = request.AuthorizedCircles,
        CircleMemberPermissionGrant = request.CircleMemberPermissionGrant
    };

    /// <summary>
    /// Registers every built-in app.  Idempotent.
    /// </summary>
    public async Task EnsureAppsAsync(IOdinContext odinContext)
    {
        foreach (var app in BuiltinApps.Builtin)
        {
            if (!Registrations.TryGetValue(app.AppId, out var request))
            {
                throw new OdinSystemException(
                    $"App '{app.Name}' is built-in but has no registration request");
            }

            if (await appRegistrationService.GetAppRegistration(request.AppId, odinContext) != null)
            {
                continue;
            }

            if (!OdinSlug.IsValid(app.AppSlug))
            {
                throw new OdinSystemException(
                    $"App '{app.Name}' has no valid slug ('{app.AppSlug}'); it cannot be registered");
            }

            logger.LogDebug("Registering built-in app {app} as {slug}", app.Name, app.AppSlug);
            await appRegistrationService.RegisterAppAsync(WithSlug(request, app.AppSlug), odinContext);
        }
    }
}
