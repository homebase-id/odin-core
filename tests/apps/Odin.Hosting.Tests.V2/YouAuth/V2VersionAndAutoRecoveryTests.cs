#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Registry;
using Version = Odin.Services.Version;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// Two open routes the apps reach with no session in mind: the version link, and the first-run page's recovery check. A
/// browser may still send a cookie, which must change nothing: the version is opened as a page, and the recovery check
/// sends its query string in the clear.
/// </summary>
[TestFixture]
public class V2VersionAndAutoRecoveryTests : V2Fixture
{
    private const string V1Version = "/api/v1/version";
    private const string V1CanUseAutoRecovery = "/api/owner/v1/config/can-use-auto-recovery";
    private const string V2CanUseAutoRecovery = UnifiedApiRouteConstants.Configuration + "/can-use-auto-recovery";

    private string Frodo => PrimaryIdentity;

    private IIdentityRegistry Registry => Host.Server.Services.GetRequiredService<IIdentityRegistry>();

    private async Task<HttpResponseMessage> GetAsync(string path, params (string name, string value)[] cookies)
    {
        using var client = Host.CreateClient();
        return await client.SendAsync(SignInHttp.Request(HttpMethod.Get, Frodo, path, cookies));
    }

    private static async Task<string> BodyWithoutCorrelationId(HttpResponseMessage response) =>
        Regex.Replace(await response.Content.ReadAsStringAsync(), "\"correlationId\":\"[^\"]*\"", "");

    [Test]
    public async Task TheVersionAnswersInPlainTextWhateverTheBrowserHolds()
    {
        var owner = await LoginAsOwner(Frodo);
        var live = owner.Token.ToString();
        var loggedOut = (await LoginAsOwner(Frodo)).Token;
        var logout = await GetAsync($"{SignInHttp.V2OwnerAuth}/logout", (SignInHttp.OwnerCookie, loggedOut.ToString()));
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        (string name, string value)[][] browsers =
        [
            [],
            [(SignInHttp.OwnerCookie, live)],
            [(SignInHttp.OwnerCookie, loggedOut.ToString())],
        ];

        foreach (var cookies in browsers)
        {
            var v1 = await GetAsync(V1Version, cookies);
            var v2 = await GetAsync(UnifiedApiRouteConstants.Version, cookies);

            Assert.That(v2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await v2.Content.ReadAsStringAsync(), Is.EqualTo(Version.VersionText));
            Assert.That(await v2.Content.ReadAsStringAsync(), Is.EqualTo(await v1.Content.ReadAsStringAsync()));
            Assert.That(v2.Content.Headers.ContentType?.MediaType, Is.EqualTo(v1.Content.Headers.ContentType?.MediaType));
        }
    }

    [Test]
    public async Task AnUnknownFirstRunTokenAnswersAsOnV1()
    {
        var token = Guid.NewGuid();

        var v1 = await GetAsync($"{V1CanUseAutoRecovery}?frt={token}");
        var v2 = await GetAsync($"{V2CanUseAutoRecovery}?frt={token}");

        Assert.That(v2.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
        Assert.That(v2.StatusCode, Is.EqualTo(v1.StatusCode));
        Assert.That(await BodyWithoutCorrelationId(v2), Is.EqualTo(await BodyWithoutCorrelationId(v1)));
    }

    [Test]
    public async Task AValidFirstRunTokenAnswersAsOnV1_AndACookieChangesNothing()
    {
        var owner = await LoginAsOwner(Frodo);
        var token = Guid.NewGuid();

        // The registry is not reset between tests, so put the registration back as it was
        var registration = await Registry.GetAsync(Frodo);
        var original = registration.FirstRunToken;
        registration.FirstRunToken = token;

        try
        {
            var v1 = await GetAsync($"{V1CanUseAutoRecovery}?frt={token}");
            var v2 = await GetAsync($"{V2CanUseAutoRecovery}?frt={token}");
            var withCookie = await GetAsync($"{V2CanUseAutoRecovery}?frt={token}", (SignInHttp.OwnerCookie, owner.Token.ToString()));
            var unknown = await GetAsync($"{V2CanUseAutoRecovery}?frt={Guid.NewGuid()}");

            Assert.That(v2.StatusCode, Is.EqualTo(v1.StatusCode));
            Assert.That(await v2.Content.ReadAsStringAsync(), Is.EqualTo(await v1.Content.ReadAsStringAsync()));
            Assert.That(withCookie.StatusCode, Is.EqualTo(v2.StatusCode));
            Assert.That(await withCookie.Content.ReadAsStringAsync(), Is.EqualTo(await v2.Content.ReadAsStringAsync()));
            Assert.That(await v2.Content.ReadAsStringAsync(), Is.Not.EqualTo(await unknown.Content.ReadAsStringAsync()),
                "the token must have been recognized");
        }
        finally
        {
            registration.FirstRunToken = original;
        }
    }
}
