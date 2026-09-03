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
    /// Drives that must exist even though their app is not built-in, because the system circles grant
    /// them and issuing a grant for an absent drive throws
    /// (<c>ExchangeGrantService</c> resolves with <c>failIfInvalid: true</c>).
    /// </summary>
    /// <remarks>
    /// <b>Temporary.</b>  This is not an ownership fact, which is why the tree cannot express it.  It
    /// goes when the system circles retire.
    /// </remarks>
    private static readonly IReadOnlyList<CreateDriveRequest> SystemCircleCarryOverDrives =
    [
        BuiltinDrives.ListsDrive,
        BuiltinDrives.MomentsDrive,

        // Mail joined this list when its app left BuiltinApps.Builtin.  CircleConstants grants
        // MailDrive from both system circles, so without the drive here identity setup throws
        // invalidGrantNonExistingDrive before it finishes -- the same reason Lists and Moments are
        // here.  Remove it with the other two when the system circles retire.
        BuiltinDrives.MailDrive
    ];

    /// <summary>
    /// The registration for each built-in app.  Not on the tree yet: a registration carries
    /// <c>AuthorizedCircles</c>, and Chat's and Mail's point at the system circles, which the tree
    /// excludes.  Derivable once those retire.
    /// </summary>
    private static readonly IReadOnlyDictionary<Guid, AppRegistrationRequest> Registrations =
        new Dictionary<Guid, AppRegistrationRequest>
        {
            [SystemAppConstants.ChatAppId] = SystemAppConstants.ChatAppRegistrationRequest,
            [SystemAppConstants.MailAppId] = SystemAppConstants.MailAppRegistrationRequest,
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
    /// <item><b>Drives, non-anonymous first.</b>  Creating an anonymous-read drive makes
    /// <c>CircleNetworkService.HandleDriveAdded</c> grant read on it to the two system circles, and every
    /// drive those circles already grant must exist by then.  All six are non-anonymous, so ordering on
    /// that flag satisfies it by construction rather than by hand-sorting the list.  The system circles
    /// themselves are created by the caller before this runs -- if they were not,
    /// <c>HandleDriveAdded</c> would log a warning and silently skip the grant.</item>
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
    /// Creates the drives of every built-in app, plus the system-circle carry-overs.  Idempotent.
    /// </summary>
    public async Task EnsureDrivesAsync(IOdinContext odinContext)
    {
        // Non-anonymous first -- see EnsureAllAsync for why.
        var drives = BuiltinApps.SeededDrives
            .Concat(SystemCircleCarryOverDrives)
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

            await driveManager.CreateDriveAsync(request, odinContext);
        }
    }

    /// <summary>
    /// Creates the circles owned by every built-in app.  Idempotent.
    /// </summary>
    /// <remarks>
    /// The two system circles are not here: they belong to no app, so the tree does not carry them and
    /// <c>CircleDefinitionService.CreateSystemCirclesAsync</c> still provisions them.
    /// </remarks>
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
