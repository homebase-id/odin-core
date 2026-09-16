#nullable enable
using System;
using System.Collections.Generic;
using Odin.Services.Apps.V2;

namespace Odin.Services.Authorization.BundleTokens;

/// <summary>
/// Everything an app sends to get one bundle token for several apps, in one go: each app either with a
/// manifest (installed or updated as part of the authorization) or by id alone (already registered, e.g.
/// a built-in app).
/// </summary>
public class BundleAuthorizationRequest
{
    public Guid PrimaryAppId { get; set; }

    public List<BundleAppRequest> Apps { get; set; } = [];

    public string FriendlyName { get; set; } = "";

    /// <summary>The client's ECC P-384 public key, as YouAuth sends it.  Not needed for a preview.</summary>
    public string JwkBase64UrlPublicKey { get; set; } = "";

    public string? RedirectUri { get; set; }
}

public class BundleAppRequest
{
    public Guid AppId { get; set; }

    /// <summary>Null for an app that must already be registered.</summary>
    public AppManifestV2? Manifest { get; set; }
}

public enum BundleAppAction
{
    /// <summary>Registered and already matches (or no manifest was sent).</summary>
    None = 0,
    Install = 1,
    Update = 2
}

public class BundleAppPreview
{
    public Guid AppId { get; init; }
    public string Name { get; init; } = "";
    public string AppSlug { get; init; } = "";
    public bool IsPrimary { get; init; }
    public bool IsRegistered { get; init; }
    public bool IsReserved { get; init; }
    public bool IsRevoked { get; init; }
    public bool HasManifest { get; init; }
    public BundleAppAction Action { get; init; }

    /// <summary>The manifest's validation, when one was sent.</summary>
    public AppRegistrationValidationResult? Validation { get; init; }

    /// <summary>Problems with this app that are not about its manifest (missing, revoked, conflicts with another app in the request).</summary>
    public List<AppRegistrationProblem> Problems { get; init; } = [];
}

public class BundleAuthorizationPreview
{
    public bool IsValid { get; init; }
    public List<BundleAppPreview> Apps { get; init; } = [];

    /// <summary>Problems with the request as a whole: primary app, redirect, friendly name, size.</summary>
    public List<AppRegistrationProblem> Problems { get; init; } = [];
}
