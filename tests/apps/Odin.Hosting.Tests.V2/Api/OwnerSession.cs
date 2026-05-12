using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using OwnerPaths = Odin.Services.Authentication.Owner.OwnerApiPathConstants;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// An owner caller bound to one identity on a running <see cref="OdinHost"/>. Bundles the
/// issued <see cref="ClientAuthenticationToken"/>, shared secret, an <see cref="InProcessApiClientFactory"/>,
/// pre-built V2 client wrappers, and a V1 <see cref="OwnerAdmin"/> facade for the admin endpoints
/// (drives, apps, circles, YouAuth domains) that V2 doesn't yet expose.
/// </summary>
public sealed class OwnerSession : IV2Caller
{
    public OdinId Identity { get; }
    public ClientAuthenticationToken Token { get; }
    public SensitiveByteArray SharedSecret { get; }

    /// <summary>
    /// Factory for V2 API calls and any V1 admin Refit interface with a fully-qualified RootPath
    /// (e.g. <c>IRefitDriveManagement</c>, <c>IRefitOwnerAppRegistration</c>, <c>IRefitYouAuthDomainRegistration</c>).
    /// BaseAddress = <c>https://{identity}/</c>; Refit string-concatenates its path on top.
    /// </summary>
    public InProcessApiClientFactory Factory { get; }

    /// <summary>
    /// Factory for V1 admin Refit interfaces whose RootPath is relative to <c>/api/owner/v1</c>
    /// (e.g. <c>IRefitUniversalCircleDefinition</c> with RootPath <c>/circles/definitions</c>).
    /// BaseAddress = <c>https://{identity}/api/owner/v1</c>.
    /// </summary>
    internal InProcessApiClientFactory V1AdminFactory { get; }

    public AuthV2Client Auth { get; }
    public OwnerAdmin Admin { get; }

    private OwnerSession(OdinHost host, string identity, ClientAuthenticationToken token, SensitiveByteArray sharedSecret)
    {
        Identity = (OdinId)identity;
        Token = token;
        SharedSecret = sharedSecret;
        Factory = new InProcessApiClientFactory(host, OwnerAuthConstants.CookieName, token, sharedSecret);
        V1AdminFactory = new InProcessApiClientFactory(host, OwnerAuthConstants.CookieName, token, sharedSecret, basePath: OwnerPaths.BasePathV1);
        Auth = new AuthV2Client(Identity, Factory);
        Admin = new OwnerAdmin(this);
    }

    public static async Task<OwnerSession> LoginAsync(OdinHost host, string identity)
    {
        var (token, secret) = await OwnerLogin.RunAsync(host, identity);
        return new OwnerSession(host, identity, token, secret);
    }

    /// <summary>
    /// Owner-authenticated HttpClient + shared secret for V1 admin Refit interfaces whose RootPath
    /// is fully qualified (starts with <c>/api/owner/v1/</c>). Each call returns a fresh client.
    /// </summary>
    internal (HttpClient client, SensitiveByteArray sharedSecret) NewAdminHttpClient()
    {
        var http = Factory.CreateHttpClient(Identity, out var ss);
        return (http, ss);
    }
}
