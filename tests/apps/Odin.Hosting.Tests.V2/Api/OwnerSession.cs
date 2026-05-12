using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// An owner caller bound to one identity on a running <see cref="OdinHost"/>. Bundles the
/// issued <see cref="ClientAuthenticationToken"/>, shared secret, an <see cref="InProcessApiClientFactory"/>,
/// and pre-built V2 client wrappers — created once at login and reused across the session.
/// </summary>
public sealed class OwnerSession
{
    public OdinId Identity { get; }
    public ClientAuthenticationToken Token { get; }
    public SensitiveByteArray SharedSecret { get; }
    public InProcessApiClientFactory Factory { get; }
    public AuthV2Client Auth { get; }

    private OwnerSession(OdinHost host, string identity, ClientAuthenticationToken token, SensitiveByteArray sharedSecret)
    {
        Identity = (OdinId)identity;
        Token = token;
        SharedSecret = sharedSecret;
        Factory = new InProcessApiClientFactory(host, OwnerAuthConstants.CookieName, token, sharedSecret);
        Auth = new AuthV2Client(Identity, Factory);
    }

    public static async Task<OwnerSession> LoginAsync(OdinHost host, string identity)
    {
        var (token, secret) = await OwnerLogin.RunAsync(host, identity);
        return new OwnerSession(host, identity, token, secret);
    }
}
