#nullable enable
using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Apps.V2;

/// <summary>
/// Everything an app asks for when it is registered through V2: the V1 registration fields, plus the
/// drives and circles the app owns (docs/app-registration-v2-plan-simplified.md).
/// </summary>
public class AppManifestV2
{
    public Guid AppId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Required in V2; immutable once registered.</summary>
    public string AppSlug { get; set; } = "";

    public string? CorsHostName { get; set; }

    /// <summary>Identity-wide permission keys the app itself holds.</summary>
    public PermissionSet? PermissionSet { get; set; }

    /// <summary>
    /// Drives the app is explicitly granted.  Owned drives need not be listed: owning a drive grants
    /// ReadWrite on it unless an entry here says otherwise.
    /// </summary>
    public List<DriveGrantRequest>? Drives { get; set; }

    public List<Guid>? AuthorizedCircles { get; set; }

    public PermissionSetGrantRequest? CircleMemberPermissionGrant { get; set; }

    public List<OwnedDrive>? OwnedDrives { get; set; }

    public List<OwnedCircle>? OwnedCircles { get; set; }
}

/// <summary>
/// A drive the app owns.  <c>CreateDriveRequest</c> minus <c>AppId</c>: the owner always comes from the
/// registration, so a request cannot name another one.
/// </summary>
public class OwnedDrive
{
    public string Name { get; set; } = "";
    public TargetDrive TargetDrive { get; set; } = null!;
    public string? Metadata { get; set; }
    public bool AllowAnonymousReads { get; set; }
    public bool AllowSubscriptions { get; set; }
    public bool AllowCdn { get; set; }
    public bool OwnerOnly { get; set; }

    /// <summary>Required in V2.</summary>
    public string DriveSlug { get; set; } = "";

    /// <summary>Required in V2.</summary>
    public string DriveTypeSlug { get; set; } = "";

    public Dictionary<string, string>? Attributes { get; set; }
}

/// <summary>
/// A circle the app owns.  <c>CreateCircleRequest</c> minus <c>AppId</c>.
/// </summary>
public class OwnedCircle
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<DriveGrantRequest>? DriveGrants { get; set; }
    public PermissionSet? Permissions { get; set; }
    public CircleGrantOn GrantOn { get; set; } = CircleGrantOn.None;
    public CircleDesignation Designation { get; set; } = CircleDesignation.Personal;
    public string? Emoji { get; set; }
}

public class AddOwnedResourcesRequest
{
    public List<OwnedDrive>? OwnedDrives { get; set; }
    public List<OwnedCircle>? OwnedCircles { get; set; }
}

public class UpdateAppPermissionsV2Request
{
    public PermissionSet? PermissionSet { get; set; }
    public List<DriveGrantRequest>? Drives { get; set; }
}

public class UpdateAuthorizedCirclesV2Request
{
    public List<Guid>? AuthorizedCircles { get; set; }
    public PermissionSetGrantRequest? CircleMemberPermissionGrant { get; set; }
}

/// <summary>Null leaves a field unchanged.</summary>
public class UpdateOwnedDriveRequest
{
    public bool? AllowAnonymousReads { get; set; }
    public bool? AllowSubscriptions { get; set; }
    public bool? AllowCdn { get; set; }
    public bool? IsArchived { get; set; }
    public string? Metadata { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
}

public class AppRegistrationProblem
{
    /// <summary>Stable machine-readable code, e.g. <c>slugTaken</c>.</summary>
    public string Code { get; init; } = "";

    /// <summary>Which part of the manifest, e.g. <c>ownedDrives[1]</c>.</summary>
    public string Subject { get; init; } = "";

    public string Message { get; init; } = "";
}

/// <summary>One drive the app will reach, as the owner console shows it.</summary>
public class DriveAccessEntry
{
    public TargetDrive TargetDrive { get; init; } = null!;
    public DrivePermission Permission { get; init; }

    /// <summary>Display name, when the drive exists or is declared.</summary>
    public string? DriveName { get; init; }

    /// <summary>The drive's owning app; null for an owner drive.</summary>
    public Guid? OwningAppId { get; init; }

    public string? OwningAppName { get; init; }
}

public class AppRegistrationDiff
{
    public List<OwnedDrive> DrivesToCreate { get; init; } = [];
    public List<OwnedDrive> DrivesAlreadyOwned { get; init; } = [];
    public List<OwnedCircle> CirclesToCreate { get; init; } = [];
    public List<OwnedCircle> CirclesAlreadyOwned { get; init; } = [];

    /// <summary>The app's drive access after the change, owned drives and circle-derived grants included.</summary>
    public List<DriveAccessEntry> DriveAccess { get; init; } = [];

    public List<DriveAccessEntry> DriveAccessGained { get; init; } = [];
    public List<DriveAccessEntry> DriveAccessLost { get; init; } = [];
    public List<int> PermissionKeysGained { get; init; } = [];
    public List<int> PermissionKeysLost { get; init; } = [];
    public List<Guid> AuthorizedCirclesAdded { get; init; } = [];
    public List<Guid> AuthorizedCirclesRemoved { get; init; } = [];
}

public class AppRegistrationValidationResult
{
    public bool IsValid => Problems.Count == 0;

    /// <summary>True when the manifest describes an app that is already registered (an update).</summary>
    public bool IsRegistered { get; init; }

    public List<AppRegistrationProblem> Problems { get; init; } = [];

    public AppRegistrationDiff Diff { get; init; } = new();
}

public class OwnedDriveInfo
{
    public Guid DriveId { get; init; }
    public TargetDrive TargetDrive { get; init; } = null!;
    public string Name { get; init; } = "";
    public string? DriveSlug { get; init; }
    public string? DriveTypeSlug { get; init; }
    public bool AllowAnonymousReads { get; init; }
    public bool AllowSubscriptions { get; init; }
    public bool AllowCdn { get; init; }
    public bool OwnerOnly { get; init; }
    public bool IsArchived { get; init; }
}

/// <summary>A registration with everything it owns.</summary>
public class AppRegistrationV2
{
    public RedactedAppRegistration Registration { get; init; } = null!;

    /// <summary>True for built-in apps and Mail; V2 cannot register or update these.</summary>
    public bool IsReserved { get; init; }

    public List<OwnedDriveInfo> OwnedDrives { get; init; } = [];
    public List<RedactedCircleDefinition> OwnedCircles { get; init; } = [];

    public UnixTimeUtc Created => Registration.Created;
}
