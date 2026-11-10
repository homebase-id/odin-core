using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// An owner caller bound to one identity on a running <see cref="OdinHost"/> (or the local dev server). Bundles the
/// issued <see cref="ClientAuthenticationToken"/>, shared secret, an <see cref="InProcessApiClientFactory"/>,
/// pre-built V2 client wrappers, and a V1 <see cref="OwnerAdmin"/> facade for the admin endpoints
/// (drives, apps, circles, YouAuth domains) that V2 doesn't yet expose.
/// </summary>
public sealed class OwnerSession : IV2Caller
{
    public OdinId Identity { get; }
    /// <summary>Where this session's requests go: the in-process host in a test, the local dev server in a fixture tool</summary>
    public IOdinTarget Target { get; }

    /// <summary>The in-process host, for a test that reaches past the API into it; a session on the dev server has none</summary>
    public OdinHost Host => Target as OdinHost
        ?? throw new InvalidOperationException($"{Identity} is on the local dev server, which has no in-process host");

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
    public V1Handles V1 { get; }
    public OwnerAdmin Admin { get; }
    public ConnectionsHandle Connections { get; }
    public FollowersHandle Followers { get; }

    /// <summary>
    /// Test-only drain hooks scoped to this owner. Outbox drain / status reads delegate to the
    /// tenant's <see cref="ITestSync"/>; <see cref="ITestSync.ProcessInboxAsync"/> goes through the
    /// owner's HTTP endpoint so inbox processing runs under the owner's permissions context (see
    /// <see cref="OwnerSync"/> for why that matters). Cached for the lifetime of this session.
    /// </summary>
    public ITestSync Sync { get; }

    private OwnerSession(IOdinTarget target, string identity, ClientAuthenticationToken token, SensitiveByteArray sharedSecret)
    {
        Identity = (OdinId)identity;
        Target = target;
        Token = token;
        SharedSecret = sharedSecret;
        Factory = new InProcessApiClientFactory(target, OwnerAuthConstants.CookieName, token, sharedSecret);
        Auth = new AuthV2Client(Identity, Factory);
        Drives = new DriveHandles(Identity, Factory);
        V1 = new V1Handles(Identity, Factory);
        Admin = new OwnerAdmin(this);
        Connections = new ConnectionsHandle(this);
        Followers = new FollowersHandle(this);
        Sync = new OwnerSync(target.GetTestSync(identity), this);
    }

    /// <summary>
    /// An owner session on the local dev server, for a fixture tool: sets <paramref name="password"/> first when
    /// <paramref name="setPassword"/> (an identity no one has signed in to yet)
    /// </summary>
    public static async Task<OwnerSession> LoginToDevServerAsync(DevServer devServer, string identity, string password,
        bool setPassword)
    {
        var (token, secret) = await OwnerLogin.RunAsync(devServer, identity, password, setPassword);
        return new OwnerSession(devServer, identity, token, secret);
    }

    /// <param name="password">
    /// Defaults to <see cref="OwnerLogin.DefaultPassword"/>, which the baseline set for every
    /// preconfigured identity. Pass an explicit one for a test that has just <i>changed</i> the
    /// password out from under that default — the Shamir recovery-finalization flow — so the new
    /// session comes back as a full <see cref="OwnerSession"/> rather than a hand-built factory.
    /// </param>
    public static async Task<OwnerSession> LoginAsync(
        OdinHost host, string identity, string password = OwnerLogin.DefaultPassword)
    {
        var (token, secret) = await OwnerLogin.RunAsync(host, identity, password);
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

    /// <summary>
    /// The Refit surface <typeparamref name="T"/> as this owner, handing back the raw
    /// <c>ApiResponse</c>. Use this when a refusal is the thing under test.
    /// </summary>
    /// <remarks>
    /// <see cref="Admin"/> is arrange-only: its helpers pick opinionated defaults and throw on
    /// non-2xx, because setup that fails is a broken test rather than an expected outcome. That
    /// makes it the wrong tool once an admin endpoint becomes the system under test — you need the
    /// caller's own request shape and the unfiltered response. Reach for this instead of adding a
    /// non-throwing twin to <see cref="OwnerAdmin"/>; the twin would have to be repeated for
    /// roughly half the admin surface.
    /// </remarks>
    public T RefitFor<T>()
    {
        var (client, ss) = NewAdminHttpClient();
        return RefitCreator.RestServiceFor<T>(client, ss);
    }
}
