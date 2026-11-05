using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Notifications;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Hosting.Authentication.YouAuth;

namespace Odin.Hosting.Tests.V2.Auth;

/// <summary>
/// The V2 notification socket's cookie routes, for browsers with no bearer token, and that its bearer routes didn't
/// change. Runs over TestServer's WebSocket: the real auth, routing and handshake, but not a real upgrade on a wire;
/// that is the Kestrel fixture beside the bearer tests in <c>Odin.Hosting.Tests</c>.
/// </summary>
[TestFixture]
public class V2CookieSocketTests : V2Fixture
{
    private const string SubProtocol = "odin.notify.v1";
    private const string OwnerRoute = UnifiedApiRouteConstants.NotifySocketOwnerCookie;
    private const string AppRoute = UnifiedApiRouteConstants.NotifySocketAppCookie;

    private string SameOrigin => $"https://{PrimaryIdentity}";

    [Test]
    public async Task Owner_CompletesTheHandshake_WithItsCookie()
    {
        var owner = await LoginAsOwner();

        using var socket = await ConnectAsync(OwnerRoute, SameOrigin, Cookie(OwnerAuthConstants.CookieName, owner.Token));

        Assert.That(socket.SubProtocol, Is.EqualTo(SubProtocol));
        await AssertHandshakeAsync(socket, owner.SharedSecret.GetKey());
    }

    [Test]
    public async Task Owner_NoSubProtocolOffered_NoneIsNamedInTheAnswer()
    {
        var owner = await LoginAsOwner();

        using var socket = await ConnectAsync(OwnerRoute, SameOrigin, Cookie(OwnerAuthConstants.CookieName, owner.Token),
            offerSubProtocol: false);

        Assert.That(socket.SubProtocol, Is.Null);
        await AssertHandshakeAsync(socket, owner.SharedSecret.GetKey());
    }

    [Test]
    public async Task App_CompletesTheHandshake_WithItsCookie()
    {
        var (_, token, sharedSecret) = await SetupAppAsync();

        using var socket = await ConnectAsync(AppRoute, SameOrigin, Cookie(YouAuthConstants.AppCookieName, token));

        await AssertHandshakeAsync(socket, sharedSecret);
    }

    /// <summary>
    /// An app served from the identity's own host sends the owner cookie as well. Each route must use its own: the
    /// shared secret proves which caller the server took this for, since it decrypts with that caller's.
    /// </summary>
    [Test]
    public async Task BothCookies_EachRouteUsesItsOwn()
    {
        var (owner, token, appSharedSecret) = await SetupAppAsync();
        var both = $"{Cookie(OwnerAuthConstants.CookieName, owner.Token)}; {Cookie(YouAuthConstants.AppCookieName, token)}";

        using (var appSocket = await ConnectAsync(AppRoute, SameOrigin, both))
        {
            await AssertHandshakeAsync(appSocket, appSharedSecret);
        }

        using var ownerSocket = await ConnectAsync(OwnerRoute, SameOrigin, both);
        await AssertHandshakeAsync(ownerSocket, owner.SharedSecret.GetKey());
    }

    /// <summary>The same token reconnecting: the app path's context is cached, and must be cloned to set on it.</summary>
    [Test]
    public async Task App_ReconnectsWithTheSameToken()
    {
        var (_, token, sharedSecret) = await SetupAppAsync();
        var cookie = Cookie(YouAuthConstants.AppCookieName, token);

        using (var first = await ConnectAsync(AppRoute, SameOrigin, cookie))
        {
            await AssertHandshakeAsync(first, sharedSecret);
        }

        using var second = await ConnectAsync(AppRoute, SameOrigin, cookie);
        await AssertHandshakeAsync(second, sharedSecret);
    }

    public static IEnumerable<object[]> HostileOrigins()
    {
        yield return ["https://evil.example.com"];
        yield return ["https://sam.dotyou.cloud"]; // a sibling on the same site: SameSite=Strict lets this cookie through
        yield return ["http://frodo.dotyou.cloud"];
        yield return ["https://frodo.dotyou.cloud:8443"];
        yield return ["https://frodo.dotyou.cloud.evil.example.com"];
        yield return ["null"]; // a sandboxed iframe or a file
        yield return [""];
        yield return [null!]; // no Origin at all
    }

    [Test, TestCaseSource(nameof(HostileOrigins))]
    public async Task HostileOrigin_IsRefused_OnBothRoutes(string origin)
    {
        var (owner, token, _) = await SetupAppAsync();

        var ownerStatus = await StatusOfAsync(OwnerRoute, origin, Cookie(OwnerAuthConstants.CookieName, owner.Token));
        Assert.That(ownerStatus, Is.EqualTo(HttpStatusCode.Forbidden));

        var appStatus = await StatusOfAsync(AppRoute, origin, Cookie(YouAuthConstants.AppCookieName, token));
        Assert.That(appStatus, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    public static IEnumerable<TestCaseData> OriginRules()
    {
        // the identity's own host, any casing, its port stated or not
        yield return new TestCaseData("https://frodo.dotyou.cloud", "frodo.dotyou.cloud", null).Returns(true);
        yield return new TestCaseData("https://FRODO.dotyou.cloud", "frodo.dotyou.cloud", null).Returns(true);
        yield return new TestCaseData("https://frodo.dotyou.cloud:443", "frodo.dotyou.cloud", null).Returns(true);
        yield return new TestCaseData("https://frodo.dotyou.cloud", "frodo.dotyou.cloud:443", null).Returns(true);
        yield return new TestCaseData("https://frodo.dotyou.cloud:8443", "frodo.dotyou.cloud:8443", null).Returns(true);

        // the host the owner registered the app on, and only that one
        yield return new TestCaseData("https://app.example.com", "frodo.dotyou.cloud", "app.example.com").Returns(true);
        yield return new TestCaseData("https://app.example.com:3005", "frodo.dotyou.cloud", "app.example.com:3005").Returns(true);
        yield return new TestCaseData("https://app.example.com", "frodo.dotyou.cloud", "app.example.com:3005").Returns(false);
        yield return new TestCaseData("https://other.example.com", "frodo.dotyou.cloud", "app.example.com").Returns(false);
        yield return new TestCaseData("http://app.example.com", "frodo.dotyou.cloud", "app.example.com").Returns(false);

        yield return new TestCaseData("not a url", "frodo.dotyou.cloud", null).Returns(false);
    }

    [Test, TestCaseSource(nameof(OriginRules))]
    public bool OriginRule(string origin, string host, string corsHostName) =>
        V2NotificationSocketController.IsAllowedCookieOrigin(origin, new HostString(host), corsHostName);

    [Test]
    public void TwoOrigins_AreRefused()
    {
        var origins = new StringValues(["https://frodo.dotyou.cloud", "https://evil.example.com"]);

        Assert.That(V2NotificationSocketController.IsAllowedCookieOrigin(origins, new HostString("frodo.dotyou.cloud"), null),
            Is.False);
    }

    [Test]
    public async Task WrongOrMissingCookie_Is401()
    {
        var (owner, token, _) = await SetupAppAsync();
        var ownerToken = Cookie(OwnerAuthConstants.CookieName, owner.Token);
        var appToken = Cookie(YouAuthConstants.AppCookieName, token);

        // each route reads only its own cookie
        Assert.That(await StatusOfAsync(OwnerRoute, SameOrigin, appToken), Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await StatusOfAsync(AppRoute, SameOrigin, ownerToken), Is.EqualTo(HttpStatusCode.Unauthorized));

        // a token of the wrong kind under the right cookie name
        Assert.That(await StatusOfAsync(OwnerRoute, SameOrigin, Cookie(OwnerAuthConstants.CookieName, token)),
            Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await StatusOfAsync(AppRoute, SameOrigin, Cookie(YouAuthConstants.AppCookieName, owner.Token)),
            Is.EqualTo(HttpStatusCode.Unauthorized));

        // a guest's cookie gets neither
        Assert.That(await StatusOfAsync(OwnerRoute, SameOrigin, Cookie(YouAuthDefaults.XTokenCookieName, owner.Token)),
            Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await StatusOfAsync(AppRoute, SameOrigin, Cookie(YouAuthDefaults.XTokenCookieName, token)),
            Is.EqualTo(HttpStatusCode.Unauthorized));

        foreach (var route in new[] { OwnerRoute, AppRoute })
        {
            Assert.That(await StatusOfAsync(route, SameOrigin, null), Is.EqualTo(HttpStatusCode.Unauthorized), route);
            Assert.That(await StatusOfAsync(route, SameOrigin, $"{OwnerAuthConstants.CookieName}=garbage; {YouAuthConstants.AppCookieName}=garbage"),
                Is.EqualTo(HttpStatusCode.Unauthorized), route);
        }
    }

    [Test]
    public async Task ALoggedOutOwner_Is401()
    {
        var owner = await LoginAsOwner();
        var token = owner.Token;
        var loggedOut = await owner.Auth.Logout();
        Assert.That(loggedOut.IsSuccessStatusCode, Is.True);

        var status = await StatusOfAsync(OwnerRoute, SameOrigin, Cookie(OwnerAuthConstants.CookieName, token));
        Assert.That(status, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ADeletedAppClient_Is401()
    {
        var (owner, token, sharedSecret) = await SetupAppAsync();
        var appSession = AppSession.FromToken(owner, Guid.Empty, token, sharedSecret);
        var loggedOut = await appSession.Auth.Logout();
        Assert.That(loggedOut.IsSuccessStatusCode, Is.True);

        var status = await StatusOfAsync(AppRoute, SameOrigin, Cookie(YouAuthConstants.AppCookieName, token));
        Assert.That(status, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task APlainRequestToACookieRoute_IsABadRequest()
    {
        var client = Host.CreateAnonymousClient(PrimaryIdentity);

        foreach (var route in new[] { OwnerRoute, AppRoute })
        {
            var response = await client.GetAsync(route);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), route);
        }
    }

    //
    // The bearer routes, unchanged
    //

    [Test]
    public async Task Bearer_StillWorks_WithAnyOriginOrNone([Values(null, "https://evil.example.com")] string origin,
        [Values(UnifiedApiRouteConstants.NotifySocket, UnifiedApiRouteConstants.NotifySocketWasm)] string route)
    {
        var (_, token, sharedSecret) = await SetupAppAsync();

        using var socket = await ConnectAsync(route, origin, cookie: null, bearer: token);

        Assert.That(socket.SubProtocol, Is.EqualTo(SubProtocol));
        await AssertHandshakeAsync(socket, sharedSecret);
    }

    [Test]
    public async Task Bearer_RoutesDontTakeACookie([Values(UnifiedApiRouteConstants.NotifySocket,
        UnifiedApiRouteConstants.NotifySocketWasm)] string route)
    {
        var (owner, token, _) = await SetupAppAsync();
        var both = $"{Cookie(OwnerAuthConstants.CookieName, owner.Token)}; {Cookie(YouAuthConstants.AppCookieName, token)}";

        Assert.That(await StatusOfAsync(route, SameOrigin, both), Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    //
    // Preauth
    //

    [Test]
    public async Task PreAuth_SetsTheCookie_AsAStrictHostCookieForTheWholeSite()
    {
        var (_, token, sharedSecret) = await SetupAppAsync();
        var client = Host.CreateAnonymousClient(PrimaryIdentity);
        using var request = new HttpRequestMessage(HttpMethod.Post, UnifiedApiRouteConstants.NotifyPreAuth);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.ToPortableBytes64());

        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var setCookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.That(setCookie, Does.StartWith($"{YouAuthConstants.AppCookieName}="));
        Assert.That(setCookie.ToLowerInvariant(), Does.Contain("path=/;").And.Contain("secure").And.Contain("samesite=strict")
            .And.Contain("httponly"));
        Assert.That(setCookie.ToLowerInvariant(), Does.Not.Contain("domain="));

        // The cookie it set is what the socket takes
        var cookie = setCookie.Split(';')[0];
        using var socket = await ConnectAsync(AppRoute, SameOrigin, cookie);
        await AssertHandshakeAsync(socket, sharedSecret);
    }

    /// <summary>The body a js-lib client with a shared secret might send is ignored, not decrypted.</summary>
    [Test]
    public async Task PreAuth_IgnoresABody_AndAnOwnerCookieBesideTheBearer()
    {
        var (owner, token, _) = await SetupAppAsync();
        var client = Host.CreateAnonymousClient(PrimaryIdentity);
        using var request = new HttpRequestMessage(HttpMethod.Post, UnifiedApiRouteConstants.NotifyPreAuth);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.ToPortableBytes64());
        request.Headers.Add("Cookie", Cookie(OwnerAuthConstants.CookieName, owner.Token));
        request.Content = new StringContent("{\"iv\":\"AA==\",\"data\":\"AA==\"}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.GetValues("Set-Cookie").Single(), Does.Contain(token.ToString()));
    }

    [Test]
    public async Task PreAuth_ExtendsAnExistingCookie()
    {
        var (_, token, _) = await SetupAppAsync();
        var client = Host.CreateAnonymousClient(PrimaryIdentity);
        using var request = new HttpRequestMessage(HttpMethod.Post, UnifiedApiRouteConstants.NotifyPreAuth);
        request.Headers.Add("Cookie", Cookie(YouAuthConstants.AppCookieName, token));

        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.Contains("Set-Cookie"), Is.True);
    }

    [Test]
    public async Task PreAuth_RefusesAnythingButAnAppToken()
    {
        var owner = await LoginAsOwner();
        var client = Host.CreateAnonymousClient(PrimaryIdentity);

        var noToken = await client.PostAsync(UnifiedApiRouteConstants.NotifyPreAuth, null);
        Assert.That(noToken.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var ownerToken = new HttpRequestMessage(HttpMethod.Post, UnifiedApiRouteConstants.NotifyPreAuth);
        ownerToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token.ToPortableBytes64());
        var response = await client.SendAsync(ownerToken);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.Contains("Set-Cookie"), Is.False);
    }

    //

    private static string Cookie(string name, ClientAuthenticationToken token) => $"{name}={token}";

    private async Task<(OwnerSession Owner, ClientAuthenticationToken Token, byte[] SharedSecret)> SetupAppAsync()
    {
        var owner = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "an app's drive");

        var appId = Guid.NewGuid();
        await owner.Admin.RegisterApp(appId, TestUtils.CreatePermissionGrantRequest(drive, DrivePermission.Read));
        var (token, sharedSecret) = await owner.Admin.RegisterAppClient(appId);
        return (owner, token, sharedSecret);
    }

    private async Task<WebSocket> ConnectAsync(string route, string origin, string cookie,
        ClientAuthenticationToken bearer = null, bool offerSubProtocol = true)
    {
        var client = Host.Server.CreateWebSocketClient();
        if (offerSubProtocol)
        {
            client.SubProtocols.Add(SubProtocol);
        }

        if (bearer != null)
        {
            client.SubProtocols.Add("odin.bearer." + V2Base64Url(bearer.ToPortableBytes()));
        }

        client.ConfigureRequest = request =>
        {
            if (origin != null)
            {
                request.Headers.Origin = origin;
            }

            if (cookie != null)
            {
                request.Headers.Cookie = cookie;
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await client.ConnectAsync(new Uri($"wss://{PrimaryIdentity}{route}"), cts.Token);
    }

    private async Task<HttpStatusCode> StatusOfAsync(string route, string origin, string cookie)
    {
        try
        {
            using var socket = await ConnectAsync(route, origin, cookie);
            return HttpStatusCode.SwitchingProtocols;
        }
        catch (InvalidOperationException e) when (e.Message.StartsWith("Incomplete handshake, status code: "))
        {
            return (HttpStatusCode)int.Parse(e.Message["Incomplete handshake, status code: ".Length..]);
        }
    }

    private static string V2Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// The first command is encrypted with the caller's shared secret and so is the answer, so this also proves which
    /// caller the server took the socket for.
    /// </summary>
    private static async Task AssertHandshakeAsync(WebSocket socket, byte[] sharedSecret)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var command = new SocketCommand
        {
            Command = SocketCommandType.EstablishConnectionRequest,
            Data = OdinSystemSerializer.Serialize(new EstablishConnectionOptions { Drives = [] })
        };
        var encrypted = SharedSecretEncryptedPayload.Encrypt(
            OdinSystemSerializer.Serialize(command).ToUtf8ByteArray(), sharedSecret.ToSensitiveByteArray());
        await socket.SendAsync(OdinSystemSerializer.Serialize(encrypted).ToUtf8ByteArray(), WebSocketMessageType.Text, true, cts.Token);

        var buffer = new byte[1024 * 8];
        var received = await socket.ReceiveAsync(buffer, cts.Token);
        Assert.That(received.MessageType, Is.EqualTo(WebSocketMessageType.Text));

        var payload = OdinSystemSerializer.Deserialize<ClientNotificationPayload>(buffer[..received.Count].ToStringFromUtf8Bytes());
        Assert.That(payload!.IsEncrypted, Is.True);
        var response = OdinSystemSerializer.Deserialize<EstablishConnectionResponse>(
            SharedSecretEncryptedPayload.Decrypt(payload.Payload, sharedSecret.ToSensitiveByteArray()));
        Assert.That(response!.NotificationType, Is.EqualTo(ClientNotificationType.DeviceHandshakeSuccess));

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
    }
}
