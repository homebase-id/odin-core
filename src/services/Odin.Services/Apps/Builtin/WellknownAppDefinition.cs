using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Apps.Builtin;

/// <summary>
/// One app, with what it owns.
/// </summary>
/// <param name="AppSlug">
/// The app half of <c>/apps/{appSlug}/drives/{driveSlug}</c>.  Immutable once an identity has
/// registered the app, since other identities resolve against it.
/// </param>
/// <param name="Drives">The drives this app owns.  Not the drives it is granted -- see <see cref="DriveGrants"/>.</param>
/// <param name="Circles">The circles this app owns, drive grants included.</param>
/// <param name="Permissions">Identity-wide permission keys the app itself holds.</param>
/// <param name="Uninstallable">
/// Whether the owner may uninstall it, its drives and circles with it, even when the platform provisions it (#2138).
/// False for the core of the platform.  Stated on every entry, so a new app can't become one or the other by default.
/// </param>
public sealed record WellknownAppDefinition(
    Guid AppId,
    string Name,
    OdinSlug AppSlug,
    IReadOnlyList<CreateDriveRequest> Drives,
    IReadOnlyList<CircleDefinition> Circles,
    PermissionSet Permissions,
    bool Uninstallable);

/// <summary>
/// One app's access to one drive, which may be a drive another app owns.
/// </summary>
public sealed record AppDriveGrant(Guid AppId, TargetDrive Drive, DrivePermission Permission);
