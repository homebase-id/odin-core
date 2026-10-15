#nullable enable
using System;
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Authorization.BundleTokens;

/// <summary>
/// Issues one client token that reaches several registered apps
/// (docs/app-registration-v2-plan-simplified.md, "Bundle tokens").
/// </summary>
public class IssueBundleTokenRequest
{
    /// <summary>
    /// The default acting app and the source of the token's CORS host.  Always a member, whether or not
    /// <see cref="AppIds"/> lists it.
    /// </summary>
    public Guid PrimaryAppId { get; set; }

    public List<Guid> AppIds { get; set; } = [];

    /// <summary>Shown to the owner, e.g. "Todd's phone".</summary>
    public string FriendlyName { get; set; } = "";

    /// <summary>
    /// Where the owner console sends the browser afterwards.  When the primary app has a CORS host, the
    /// redirect must go there.
    /// </summary>
    public string? RedirectUri { get; set; }
}

public class BeginBundleTokenExchangeRequest : IssueBundleTokenRequest
{
    /// <summary>The client's ECC P-384 public key, JWK, base64url -- as in YouAuth.</summary>
    public string JwkBase64UrlPublicKey { get; set; } = "";
}

public class BeginBundleTokenExchangeResponse
{
    public Guid TokenId { get; init; }
    public string ExchangePublicKeyJwkBase64Url { get; init; } = "";
    public string ExchangeSalt64 { get; init; } = "";
}

public class BundleTokenAppInfo
{
    public Guid AppId { get; init; }
    public string Name { get; init; } = "";
    public string AppSlug { get; init; } = "";
    public bool IsPrimary { get; init; }

    /// <summary>The app registration is revoked; the token cannot reach it until it is allowed again.</summary>
    public bool IsRevoked { get; init; }

    /// <summary>The app registration was deleted.</summary>
    public bool IsMissing { get; init; }
}

public class RedactedBundleToken
{
    public Guid TokenId { get; init; }
    public string FriendlyName { get; init; } = "";
    public Guid PrimaryAppId { get; init; }
    public bool IsRevoked { get; init; }
    public UnixTimeUtc Created { get; init; }
    public UnixTimeUtc ExpiresAt { get; init; }
    public List<BundleTokenAppInfo> Apps { get; init; } = [];
}
