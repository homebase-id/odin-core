using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Follower;

namespace Youverse.Hosting.Tests.OwnerApi.Follower;

public class FollowerTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task CanFollowIdentity()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        await frodoOwnerClient.Follower.FollowIdentity(samOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo should have sam
        var frodoFollows = await frodoOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsTrue(frodoFollows.Results.Count() == 1, "frodo should only follow sam");
        Assert.IsTrue(new DotYouIdentity(frodoFollows.Results.Single()) == samOwnerClient.Identity.DotYouId);

        var followingFrodo = await frodoOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsTrue(!followingFrodo.Results.Any());

        //sam should have frodo
        var followingSam = await samOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsTrue(followingSam.Results.Count() ==1, "Sam should have one follower; frodo");
        Assert.IsTrue(new DotYouIdentity(followingSam.Results.Single()) == frodoOwnerClient.Identity.DotYouId);

        var samFollows = await samOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsTrue(!samFollows.Results.Any(), "Sam should not be following anyone");
        
        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task CanUnfollowIdentity()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);


        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task GetIdentities_Followed()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);


        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task GetFollowers()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);


        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task FailToFollowNonChannelDrive()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);


        // All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
    }
}