#nullable enable
using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// A CDN-authenticated caller bound to a given identity. The CDN flow doesn't use shared-secret
/// encryption — auth is a fixed bearer token (the host's <c>Cdn__RequiredAuthToken</c>, seeded by
/// <see cref="OdinHost"/>'s env baseline). The token must match what the host trusts; we reuse the
/// V1 fixture's static <see cref="CdnTestCase"/> token so both frameworks line up.
/// </summary>
/// <remarks>
/// CdnSession implements <see cref="IV2Caller"/> so the existing <c>Drives.Reader.GetPayloadAsync</c>
/// / <c>...GetThumbnailAsync</c> wrappers work unchanged — they go through the same Refit pipeline,
/// just with a bearer-only HttpClient. The <see cref="Cdn"/> handle additionally exposes the
/// CDN-specific health / cdn-ping endpoints.
/// </remarks>
public sealed class CdnSession : IV2Caller
{
    public OdinId Identity { get; }
    public InProcessApiClientFactory Factory { get; }
    public AuthV2Client Auth { get; }
    public DriveHandles Drives { get; }
    public CdnV2Client Cdn { get; }

    private CdnSession(OdinHost host, OdinId identity)
    {
        Identity = identity;
        Factory = new InProcessApiClientFactory(
            host,
            YouAuthDefaults.XTokenCookieName,
            BuildCdnAuthToken(),
            sharedSecret: null);
        Auth = new AuthV2Client(Identity, Factory);
        Drives = new DriveHandles(Identity, Factory);
        Cdn = new CdnV2Client(Identity, Factory);
    }

    public static CdnSession Setup(OdinHost host, string identity)
    {
        return new CdnSession(host, (OdinId)identity);
    }

    // Mirror the V1 CdnTestCase token verbatim so that the in-process host's Cdn__RequiredAuthToken
    // (set in OdinHost env baseline from CdnTestCase.GetAuthToken64()) and the bearer issued by
    // this session refer to the same logical token. Keeping the two derivations in lock-step is
    // brittle but contained: change one place, change the env baseline.
    private static ClientAuthenticationToken BuildCdnAuthToken()
    {
        return new ClientAuthenticationToken
        {
            Id = Guid.Parse("058de171-2525-45dc-b496-8eafb85a703b"),
            AccessTokenHalfKey = Guid.Parse("41a247d8-fba0-442f-8391-05df4391a4e0").ToByteArray().ToSensitiveByteArray(),
            ClientTokenType = ClientTokenType.Cdn
        };
    }
}
