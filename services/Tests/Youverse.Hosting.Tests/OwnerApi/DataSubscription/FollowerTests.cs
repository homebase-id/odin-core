using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Identity;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;

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

        // Frodo should follow Sam
        var frodoFollows = await frodoOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsTrue(frodoFollows.Results.Count() == 1, "frodo should only follow sam");
        Assert.IsTrue(new OdinId(frodoFollows.Results.Single()) == samOwnerClient.Identity.OdinId);

        var followingFrodo = await frodoOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsFalse(followingFrodo.Results.Any());

        var frodoRecordOfFollowingSam = await frodoOwnerClient.Follower.GetIdentityIFollow(samOwnerClient.Identity);
        Assert.IsTrue(frodoRecordOfFollowingSam.NotificationType == FollowerNotificationType.AllNotifications);
        Assert.IsNull(frodoRecordOfFollowingSam.Channels, "there should be no channels when notification type is all notifications");

        //sam should have frodo
        var followingSam = await samOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsTrue(followingSam.Results.Count() == 1, "Sam should have one follower; frodo");
        Assert.IsTrue(new OdinId(followingSam.Results.Single()) == frodoOwnerClient.Identity.OdinId);

        var samRecordOfFrodoFollowingHim = await samOwnerClient.Follower.GetFollower(frodoOwnerClient.Identity);
        Assert.IsTrue(samRecordOfFrodoFollowingHim.NotificationType == FollowerNotificationType.AllNotifications);
        Assert.IsNull(samRecordOfFrodoFollowingHim.Channels, "there should be no channels when notification type is all notifications");

        var samFollows = await samOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsFalse(samFollows.Results.Any(), "Sam should not be following anyone");

        //All done
        await frodoOwnerClient.Follower.UnfollowIdentity(samOwnerClient.Identity);
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }


    [Test]
    [Ignore("need to talk with michael about getting all followers regardless of drive")]
    public async Task CanFollowIdentity_SelectedChannels()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create some channels for Sam
        var channel1Drive = await samOwnerClient.Drive.CreateDrive(new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        }, "Channel 1", "", true, false, true);

        var channel2Drive = await samOwnerClient.Drive.CreateDrive(new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        }, "Channel 2", "", true, false, true);

        //Frodo will follow Sam
        await frodoOwnerClient.Follower.FollowIdentity(samOwnerClient.Identity, FollowerNotificationType.SelectedChannels, new List<TargetDrive>()
        {
            channel1Drive.TargetDriveInfo,
            channel2Drive.TargetDriveInfo
        });

        // Frodo should follow Sam
        var frodoFollows = await frodoOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsTrue(frodoFollows.Results.Count() == 1, "frodo should only follow sam");
        Assert.IsTrue(new OdinId(frodoFollows.Results.Single()) == samOwnerClient.Identity.OdinId);

        var followingFrodo = await frodoOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsFalse(followingFrodo.Results.Any(), "Frodo should have no followers");

        var frodoRecordOfFollowingSam = await frodoOwnerClient.Follower.GetIdentityIFollow(samOwnerClient.Identity);
        Assert.IsTrue(frodoRecordOfFollowingSam.NotificationType == FollowerNotificationType.SelectedChannels);
        Assert.IsTrue(frodoRecordOfFollowingSam.Channels.Count() == 2, "Frodo should follow 2 of Sam's channels");
        Assert.IsNotNull(frodoRecordOfFollowingSam.Channels.SingleOrDefault(c => c == channel1Drive.TargetDriveInfo), $"Frodo should have only one record of {nameof(channel1Drive)}");
        Assert.IsNotNull(frodoRecordOfFollowingSam.Channels.SingleOrDefault(c => c == channel2Drive.TargetDriveInfo), $"Frodo should have only one record of {nameof(channel2Drive)}");

        //sam should have frodo as a follower
        var followingSam = await samOwnerClient.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.IsTrue(followingSam.Results.Count() == 1, "Sam should have one follower; frodo");
        Assert.IsTrue(new OdinId(followingSam.Results.Single()) == frodoOwnerClient.Identity.OdinId);

        var samRecordOfFrodoFollowingHim = await samOwnerClient.Follower.GetFollower(frodoOwnerClient.Identity);
        Assert.IsTrue(samRecordOfFrodoFollowingHim.NotificationType == FollowerNotificationType.AllNotifications);
       
        Assert.IsTrue(samRecordOfFrodoFollowingHim.Channels.Count() == 2, "Frodo should follow 2 of Sam's channels");
        Assert.IsNotNull(samRecordOfFrodoFollowingHim.Channels.SingleOrDefault(c => c == channel1Drive.TargetDriveInfo), $"Frodo should have only one record of {nameof(channel1Drive)}");
        Assert.IsNotNull(samRecordOfFrodoFollowingHim.Channels.SingleOrDefault(c => c == channel2Drive.TargetDriveInfo), $"Frodo should have only one record of {nameof(channel2Drive)}");

        var samFollows = await samOwnerClient.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.IsFalse(samFollows.Results.Any(), "Sam should not be following anyone");

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
        Assert.IsTrue(frodoFollowsPippin.OdinId == pippinOwnerClient.Identity.OdinId);

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