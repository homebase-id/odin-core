using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.DataSubscription;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;

namespace Odin.Hosting.Tests.V2.Ported.Follow;

/// <summary>
/// People you may know: Frodo's server asks his connections, over peer, for their own lists. Each connection's own
/// settings decide what it shares: connection lists are shared with connections by default, follows are not.
/// </summary>
/// <remarks>
/// A connection's permissions are cached on its host for up to an hour (<c>OdinContextCache</c>), and a settings
/// change does not clear that, so a fixture that changes Sam's sharing does so before Frodo's first call: see
/// <see cref="FollowerSuggestionSharedFollowsV2Tests"/>, on its own host.
/// </remarks>
[TestFixture]
public class FollowerSuggestionV2Tests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry, Identities.Pippin];

    [Test]
    public async Task SuggestsYourConnectionsConnections_AndTheirFollowsOnlyWhereShared()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);
        var pippin = await LoginAsOwner(Identities.Pippin);

        // Frodo - Sam - Merry; Sam follows Pippin
        await DataSubscriptionScenario.ConnectAsync(frodo, sam);
        await DataSubscriptionScenario.ConnectAsync(sam, merry);
        await sam.Followers.FollowAsync(pippin.Identity);

        var suggestions = new V2FollowerClient(frodo.Identity, frodo.Factory);

        var first = await suggestions.GetSuggestionsAsync();
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var merryEntry = first.Content!.Results.SingleOrDefault(s => s.OdinId == merry.Identity.DomainName);
        Assert.That(merryEntry, Is.Not.Null, "Sam shares his connections with connections by default");
        Assert.That(merryEntry!.ViaConnections, Is.EqualTo(new[] { sam.Identity.DomainName }));
        Assert.That(first.Content.Results.Select(s => s.OdinId), Does.Not.Contain(frodo.Identity.DomainName),
            "never yourself, though Sam lists Frodo among his connections");
        Assert.That(first.Content.Results.Select(s => s.OdinId), Does.Not.Contain(sam.Identity.DomainName),
            "never someone you are already connected to");
        Assert.That(first.Content.Results.Select(s => s.OdinId), Does.Not.Contain(pippin.Identity.DomainName),
            "Sam's follows are not shared with connections by default");
        Assert.That(first.Content.Asked, Is.EqualTo(1));
        Assert.That(first.Content.Answered, Is.EqualTo(1));

        // Someone Frodo already follows is not suggested
        await frodo.Followers.FollowAsync(merry.Identity);
        var third = await suggestions.GetSuggestionsAsync();
        Assert.That(third.Content!.Results.Select(s => s.OdinId), Does.Not.Contain(merry.Identity.DomainName));
    }

    [Test]
    public async Task AGuestCannotAskForSuggestions()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var guest = await GuestSession.SetupAsync(frodo, new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var response = await new V2FollowerClient(guest.Identity, guest.Factory).GetSuggestionsAsync();
        Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
    }
}

/// <summary>Sam shares who he follows with his connections, so his follows lead Frodo to Pippin.</summary>
[TestFixture]
public class FollowerSuggestionSharedFollowsV2Tests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin];

    [Test]
    public async Task AConnectionThatSharesItsFollows_LeadsToWhoItFollows()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);

        // Before Frodo's first call to Sam: Sam's answer to him is cached once made
        var flag = await sam.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.ConnectedIdentitiesCanViewWhoIFollow, "true");
        Assert.That(flag.IsSuccessStatusCode, Is.True);

        await DataSubscriptionScenario.ConnectAsync(frodo, sam);
        await sam.Followers.FollowAsync(pippin.Identity);

        var response = await new V2FollowerClient(frodo.Identity, frodo.Factory).GetSuggestionsAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var pippinEntry = response.Content!.Results.SingleOrDefault(s => s.OdinId == pippin.Identity.DomainName);
        Assert.That(pippinEntry, Is.Not.Null, "Sam shares his follows, so they lead to Pippin");
        Assert.That(pippinEntry!.ViaFollows, Is.EqualTo(new[] { sam.Identity.DomainName }));
    }
}
