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
using Odin.Services.Background.Testing;

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
    public OdinHost Host { get; }
    public ClientAuthenticationToken Token { get; }
    public SensitiveByteArray SharedSecret { get; }

    /// <summary>
    /// Single factory used for every Refit interface — V2 (absolute paths) and V1 admin (relative
    /// or absolute). BaseAddress = <c>https://{identity}/</c>; the factory's path-normalizing
    /// handler rewrites V1-relative paths to include the <c>/api/owner/v1</c> prefix.
    /// </summary>
    public InProcessApiClientFactory Factory { get; }

    public AuthV2Client Auth { get; }
    public DriveHandles Drives { get; }
    public OwnerAdmin Admin { get; }

    /// <summary>
    /// Test-only synchronous drain hooks for this tenant's peer outbox / inbox. Resolved lazily on
    /// first access from the tenant scope — see <see cref="OdinHost.GetTestSync"/>.
    /// </summary>
    public ITestSync Sync => Host.GetTestSync(Identity);

    private OwnerSession(OdinHost host, string identity, ClientAuthenticationToken token, SensitiveByteArray sharedSecret)
    {
        Identity = (OdinId)identity;
        Host = host;
        Token = token;
        SharedSecret = sharedSecret;
        Factory = new InProcessApiClientFactory(host, OwnerAuthConstants.CookieName, token, sharedSecret);
        Auth = new AuthV2Client(Identity, Factory);
        Drives = new DriveHandles(Identity, Factory);
        Admin = new OwnerAdmin(this);
    }

    public static async Task<OwnerSession> LoginAsync(OdinHost host, string identity)
    {
        var (token, secret) = await OwnerLogin.RunAsync(host, identity);
        return new OwnerSession(host, identity, token, secret);
    }

    /// <summary>
    /// Owner-authenticated HttpClient + shared secret. Each call returns a fresh client; the path
    /// handler in <see cref="InProcessApiClientFactory"/> takes care of routing V1-relative and
    /// V2-absolute Refit paths to the right endpoint.
    /// </summary>
    internal (HttpClient client, SensitiveByteArray sharedSecret) NewAdminHttpClient()
    {
        var http = Factory.CreateHttpClient(Identity, out var ss);
        return (http, ss);
    }
}
