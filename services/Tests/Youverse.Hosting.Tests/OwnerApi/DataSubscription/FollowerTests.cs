using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Identity;
using Youverse.Core.Services.DataSubscription.Follower;

namespace Youverse.Hosting.Tests.OwnerApi.DataSubscription;

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
    public async Task CanFollowIdentity_AllNotifications()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create some channels for Sam

        await frodoOwnerClient.Follower.FollowIdentity(samOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo should have sam
        var frodoFollows = await frodoOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsTrue(frodoFollows.Results.Count() == 1, "frodo should only follow sam");
        Assert.IsTrue(new OdinId(frodoFollows.Results.Single()) == samOwnerClient.Identity.OdinId);

        var followingFrodo = await frodoOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsTrue(!followingFrodo.Results.Any());

        //sam should have frodo
        var followingSam = await samOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsTrue(followingSam.Results.Count() == 1, "Sam should have one follower; frodo");
        Assert.IsTrue(new OdinId(followingSam.Results.Single()) == frodoOwnerClient.Identity.OdinId);

        var samFollows = await samOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsTrue(!samFollows.Results.Any(), "Sam should not be following anyone");

        //All done
        await frodoOwnerClient.Follower.UnfollowIdentity(samOwnerClient.Identity);
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task CanUnfollowIdentity()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        await frodoOwnerClient.Follower.FollowIdentity(samOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo should follow sam
        var frodoFollows = await frodoOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsTrue(frodoFollows.Results.Count() == 1, "frodo should only follow sam");
        Assert.IsTrue(new OdinId(frodoFollows.Results.Single()) == samOwnerClient.Identity.OdinId);

        var followingFrodo = await frodoOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsTrue(!followingFrodo.Results.Any());

        //sam should have frodo as follow
        var followingSam = await samOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsTrue(followingSam.Results.Count() == 1, "Sam should have one follower; frodo");
        Assert.IsTrue(new OdinId(followingSam.Results.Single()) == frodoOwnerClient.Identity.OdinId);

        //
        // Frodo to unfollow sam
        //
        await frodoOwnerClient.Follower.UnfollowIdentity(samOwnerClient.Identity);

        //Frodo should follow no one
        var updatedFrodoFollows = await frodoOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsTrue(updatedFrodoFollows.Results.All(f => ((OdinId)f) == samOwnerClient.Identity.OdinId), "Frodo should not follow Sam");

        //Sam should have no followers
        var updatedFollowingSam = await samOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsTrue(updatedFollowingSam.Results.All(f => ((OdinId)f) != frodoOwnerClient.Identity.OdinId), "Sam should not follow Frodo");

        //All done
        await frodoOwnerClient.Follower.UnfollowIdentity(samOwnerClient.Identity);
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task GetIdentities_I_Follow()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        await pippinOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);
        await pippinOwnerClient.Follower.FollowIdentity(samOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        //
        var pippinFollows = await pippinOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);

        Assert.IsTrue(pippinFollows.Results.Count() == 2);
        Assert.IsNotNull(pippinFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == frodoOwnerClient.Identity.OdinId), "Pippin should follow frodo");
        Assert.IsNotNull(pippinFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == samOwnerClient.Identity.OdinId), "Pippin should follow Sam");

        var frodoFollows = await frodoOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsNull(frodoFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == pippinOwnerClient.Identity.OdinId), "Frodo should not follow Pippin");

        var samFollows = await frodoOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsNull(samFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == pippinOwnerClient.Identity.OdinId), "Frodo should not follow Pippin");

        // All done
        await pippinOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
        await pippinOwnerClient.Follower.UnfollowIdentity(samOwnerClient.Identity);
    }

    [Test]
    public async Task GetFollowers_AllNotifications()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        await frodoOwnerClient.Follower.FollowIdentity(pippinOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);
        await samOwnerClient.Follower.FollowIdentity(pippinOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        //
        var pippinFollows = await pippinOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);

        Assert.IsTrue(pippinFollows.Results.Count() == 2);
        Assert.IsNotNull(pippinFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == frodoOwnerClient.Identity.OdinId), "Pippin should follow frodo");
        Assert.IsNotNull(pippinFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == samOwnerClient.Identity.OdinId), "Pippin should follow Sam");

        var frodoFollows = await frodoOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsNotNull(frodoFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == pippinOwnerClient.Identity.OdinId), "Frodo should follow Pippin");

        var frodoFollowsPippin = await frodoOwnerClient.Follower.GetIdentityIFollow(pippinOwnerClient.Identity);
        Assert.IsNotNull(frodoFollowsPippin);
        Assert.IsTrue(frodoFollowsPippin.DotYouId == pippinOwnerClient.Identity.OdinId);

        var samFollows = await frodoOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsNotNull(samFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == pippinOwnerClient.Identity.OdinId), "Sam should follow Pippin");

        // All done
        await frodoOwnerClient.Follower.UnfollowIdentity(pippinOwnerClient.Identity);
        await samOwnerClient.Follower.UnfollowIdentity(pippinOwnerClient.Identity);
    }

    [Test]
    public async Task FailToFollowYourself()
    {
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        var apiResponse = await pippinOwnerClient.Follower.FollowIdentity(pippinOwnerClient.Identity, FollowerNotificationType.AllNotifications, null, assertSuccessStatus: false);
        Assert.IsTrue(apiResponse.StatusCode == HttpStatusCode.BadRequest);

        var pippinAsFollower = await pippinOwnerClient.Follower.GetFollower(pippinOwnerClient.Identity);
        Assert.IsNull(pippinAsFollower, "Pippin cannot follow himself");
    }

    // [Test]
    // public async Task FailToFollowNonChannelDrive()
    // {
    //     var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
    //     var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
    //
    //     // All done
    //     await frodoOwnerClient.Follower.UnfollowIdentity(samOwnerClient.Identity);
    //     await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    // }
    
    //Test Permissions for Tenant Settings
    //Test that following only works for drives of type channel
}