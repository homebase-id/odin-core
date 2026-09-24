#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Membership.YouAuth;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// How long a YouAuth domain token lives, and what using it does to that.
/// </summary>
/// <remarks>
/// The owner picks the rule when they consent to a domain, and the token has to follow it:
/// <list type="bullet">
/// <item><b>Expiring</b>, with a date: the token dies on that date. Use never moves it.</item>
/// <item><b>Never</b> or <b>Always</b>: the token gets six months, and every use restarts the six
/// months, so a client in regular use is never cut off and one that went quiet is gone half a year
/// later. The same window the owner console session already has.</item>
/// </list>
/// The row in the client-registrations table is what enforces this -- it is the one store that reads
/// <c>expiresAt</c> back -- so these tests read and rewind that row directly. Rewinding is the only
/// way to stand three months in the future without a clock to fake.
/// <para>
/// Restarting the window is throttled: a window restarted within the last day is left alone, so a
/// busy client does not turn every request into a write.
/// </para>
/// </remarks>
[TestFixture]
public class YouAuthDomainTokenLifetimeTests : V2Fixture
{
    private static readonly TimeSpan SixMonths = TimeSpan.FromDays(180);

    /// <summary>
    /// Clock skew between the test computing "now" and the server doing the same a request later.
    /// </summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(10);

    [Test]
    public async Task AnExpiringConsentPutsItsDateOnTheToken()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var expiration = UnixTimeUtc.Now().AddDays(14);

        var guest = await RegisterGuestAsync(owner, ConsentRequirementType.Expiring, expiration);

        var row = await ReadRowAsync(owner, guest.TokenId);
        Assert.That(row, Is.Not.Null,
            "the token must live in the client-registrations table, the one store that enforces expiry");
        Assert.That(row!.expiresAt.milliseconds, Is.EqualTo(expiration.milliseconds).Within((long)Slack.TotalMilliseconds),
            "the owner named the date; that is when the token expires");
    }

    [TestCase(ConsentRequirementType.Never)]
    [TestCase(ConsentRequirementType.Always)]
    public async Task AConsentWithoutADateGivesTheTokenSixMonths(ConsentRequirementType consent)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var issued = UnixTimeUtc.Now();

        var guest = await RegisterGuestAsync(owner, consent);

        var row = await ReadRowAsync(owner, guest.TokenId);
        Assert.That(row, Is.Not.Null,
            "the token must live in the client-registrations table, the one store that enforces expiry");
        AssertCloseTo(row!.expiresAt, issued.AddMilliseconds((long)SixMonths.TotalMilliseconds),
            "no date from the owner means the same six months the owner console gets");
    }

    [TestCase(ConsentRequirementType.Never)]
    [TestCase(ConsentRequirementType.Always)]
    public async Task UsingTheTokenRestartsTheSixMonths(ConsentRequirementType consent)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var guest = await RegisterGuestAsync(owner, consent);

        // Three months on, and eighty days of the window left. The next use should give it six again.
        await RewindRowAsync(owner, guest.TokenId, UnixTimeUtc.Now().AddDays(80));

        var used = UnixTimeUtc.Now();
        var verify = await guest.Auth.VerifyToken();
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var row = await ReadRowAsync(owner, guest.TokenId);
        Assert.That(row, Is.Not.Null);
        AssertCloseTo(row!.expiresAt, used.AddMilliseconds((long)SixMonths.TotalMilliseconds),
            "a client still in use is never cut off: each use restarts the six months");
    }

    [Test]
    public async Task UsingTheTokenNeverMovesAFixedDate()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var guest = await RegisterGuestAsync(owner, ConsentRequirementType.Expiring, UnixTimeUtc.Now().AddDays(14));

        // Eleven days on, three left. Use must not hand it another fourteen.
        var threeDaysLeft = UnixTimeUtc.Now().AddDays(3);
        await RewindRowAsync(owner, guest.TokenId, threeDaysLeft);

        var verify = await guest.Auth.VerifyToken();
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var row = await ReadRowAsync(owner, guest.TokenId);
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.expiresAt.milliseconds, Is.EqualTo(threeDaysLeft.milliseconds),
            "the owner named a date; nothing the client does moves it");
    }

    [Test]
    public async Task AWindowRestartedWithinTheLastDayIsLeftAlone()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var guest = await RegisterGuestAsync(owner, ConsentRequirementType.Never);

        // Restarted an hour ago: a restart now would move the date by an hour, and that is not worth a write.
        var restartedAnHourAgo = UnixTimeUtc.Now().AddMilliseconds((long)SixMonths.TotalMilliseconds).AddHours(-1);
        await RewindRowAsync(owner, guest.TokenId, restartedAnHourAgo);

        var verify = await guest.Auth.VerifyToken();
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var row = await ReadRowAsync(owner, guest.TokenId);
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.expiresAt.milliseconds, Is.EqualTo(restartedAnHourAgo.milliseconds),
            "restarting the window is throttled to once a day; a recent restart is left as it was");
    }

    [Test]
    public async Task AnExpiredTokenIsRefused()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var guest = await RegisterGuestAsync(owner, ConsentRequirementType.Never);

        await RewindRowAsync(owner, guest.TokenId, UnixTimeUtc.Now().AddSeconds(-1));

        var verify = await guest.Auth.VerifyToken();
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "a token past its date is no token");

        Assert.That(await ReadRowAsync(owner, guest.TokenId), Is.Null,
            "an expired row is dropped when it is read, not left to accumulate");
    }

    [Test]
    public async Task AFixedDateIsHonouredEvenWhileTheContextIsCached()
    {
        // The permission context built for a token is cached for an hour. A token the owner gave a date
        // must not get an hour's grace from that cache, so the cache entry has to end when the token does.
        var owner = await LoginAsOwner(Identities.Frodo);
        var lifetime = TimeSpan.FromSeconds(3);
        var guest = await RegisterGuestAsync(owner, ConsentRequirementType.Expiring, UnixTimeUtc.Now().AddMilliseconds((long)lifetime.TotalMilliseconds));

        var beforeExpiry = await guest.Auth.VerifyToken();
        Assert.That(beforeExpiry.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the token is good until its date");

        await Task.Delay(lifetime + TimeSpan.FromSeconds(1));

        var afterExpiry = await guest.Auth.VerifyToken();
        Assert.That(afterExpiry.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "the cached context must not outlive the token's date");
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    private sealed record Guest(Guid TokenId, AuthV2Client Auth);

    /// <summary>
    /// A throwaway domain registered under the given consent rule, with one client under it. The
    /// domain endpoints are used straight rather than through <see cref="GuestSession"/>, which fixes
    /// the consent rule at Never -- the rule is the variable here.
    /// </summary>
    private static async Task<Guest> RegisterGuestAsync(
        OwnerSession owner,
        ConsentRequirementType consent,
        UnixTimeUtc consentExpiration = default)
    {
        var domain = new AsciiDomainName($"{Guid.NewGuid():n}-test.org");

        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();
        var registered = await svc.RegisterDomain(new YouAuthDomainRegistrationRequest
        {
            Name = $"Test_{domain.DomainName}",
            Domain = domain.DomainName,
            CircleIds = [],
            ConsentRequirements = new ConsentRequirements
            {
                ConsentRequirementType = consent,
                Expiration = consentExpiration
            }
        });
        Assert.That(registered.IsSuccessStatusCode, Is.True, $"RegisterDomain failed: {registered.StatusCode}");

        var clientReg = await owner.Admin.RegisterYouAuthClient(domain);
        var cat = ClientAccessToken.FromPortableBytes(clientReg.Content!.Data);

        var factory = new InProcessApiClientFactory(owner.Host, YouAuthDefaults.XTokenCookieName,
            cat.ToAuthenticationToken(), cat.SharedSecret.GetKey().ToSensitiveByteArray(), GuestApiPathConstantsV1.BasePathV1);

        return new Guest(cat.Id, new AuthV2Client(owner.Identity, factory));
    }

    private static async Task<ClientRegistrationsRecord?> ReadRowAsync(OwnerSession owner, Guid tokenId)
    {
        await using var scope = owner.Host.GetTenantScope(owner.Identity.DomainName).BeginLifetimeScope();
        return await scope.Resolve<TableClientRegistrations>().GetAsync(tokenId);
    }

    /// <summary>
    /// Stands the row at a chosen point in its life, then forgets the cached permission context so the
    /// next request has to look at the row again.
    /// </summary>
    private static async Task RewindRowAsync(OwnerSession owner, Guid tokenId, UnixTimeUtc expiresAt)
    {
        var tenant = owner.Host.GetTenantScope(owner.Identity.DomainName);
        await using (var scope = tenant.BeginLifetimeScope())
        {
            var table = scope.Resolve<TableClientRegistrations>();
            var row = await table.GetAsync(tokenId);
            Assert.That(row, Is.Not.Null, "cannot rewind a token that is not in the client-registrations table");
            row!.expiresAt = expiresAt;
            await table.UpsertAsync(row);
        }

        await tenant.Resolve<OdinContextCache>().ResetAsync();
    }

    private static void AssertCloseTo(UnixTimeUtc actual, UnixTimeUtc expected, string because)
    {
        Assert.That(actual.milliseconds, Is.EqualTo(expected.milliseconds).Within((long)Slack.TotalMilliseconds), because);
    }
}
