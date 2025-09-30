using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.OwnerApi.DataSubscription.Follower;

public class OwnerFollowerTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise, TestIdentities.Pippin });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }


    [Test]
    public async Task CanFollowIdentity_AllNotifications()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create some channels for Sam

        await frodoOwnerClient.OwnerFollower.FollowIdentity(samOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo should follow Sam
        var frodoFollows = await frodoOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);
        ClassicAssert.IsTrue(frodoFollows.Results.Count() == 1, "frodo should only follow sam");
        ClassicAssert.IsTrue(new OdinId(frodoFollows.Results.Single()) == samOwnerClient.Identity.OdinId);

        var followingFrodo = await frodoOwnerClient.OwnerFollower.GetIdentitiesFollowingMe(string.Empty);
        ClassicAssert.IsFalse(followingFrodo.Results.Any());

        var frodoRecordOfFollowingSam = await frodoOwnerClient.OwnerFollower.GetIdentityIFollow(samOwnerClient.Identity);
        ClassicAssert.IsTrue(frodoRecordOfFollowingSam.NotificationType == FollowerNotificationType.AllNotifications);
        ClassicAssert.IsNull(frodoRecordOfFollowingSam.Channels, "there should be no channels when notification type is all notifications");

        //sam should have frodo
        var followingSam = await samOwnerClient.OwnerFollower.GetIdentitiesFollowingMe(string.Empty);
        ClassicAssert.IsTrue(followingSam.Results.Count() == 1, "Sam should have one follower; frodo");
        ClassicAssert.IsTrue(new OdinId(followingSam.Results.Single()) == frodoOwnerClient.Identity.OdinId);

        var samRecordOfFrodoFollowingHim = await samOwnerClient.OwnerFollower.GetFollower(frodoOwnerClient.Identity);
        ClassicAssert.IsTrue(samRecordOfFrodoFollowingHim.NotificationType == FollowerNotificationType.AllNotifications);
        ClassicAssert.IsNull(samRecordOfFrodoFollowingHim.Channels, "there should be no channels when notification type is all notifications");

        var samFollows = await samOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);
        ClassicAssert.IsFalse(samFollows.Results.Any(), "Sam should not be following anyone");

        //All done
        await frodoOwnerClient.OwnerFollower.UnfollowIdentity(samOwnerClient.Identity);
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
    }


    [Test]
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
        await frodoOwnerClient.OwnerFollower.FollowIdentity(samOwnerClient.Identity, FollowerNotificationType.SelectedChannels, new List<TargetDrive>()
        {
            channel1Drive.TargetDriveInfo,
            channel2Drive.TargetDriveInfo
        });

        // Frodo should follow Sam
        var frodoFollows = await frodoOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);
        ClassicAssert.IsTrue(frodoFollows.Results.Count() == 1, "frodo should only follow sam");
        ClassicAssert.IsTrue(new OdinId(frodoFollows.Results.Single()) == samOwnerClient.Identity.OdinId);

        var followingFrodo = await frodoOwnerClient.OwnerFollower.GetIdentitiesFollowingMe(string.Empty);
        ClassicAssert.IsFalse(followingFrodo.Results.Any(), "Frodo should have no followers");

        var frodoRecordOfFollowingSam = await frodoOwnerClient.OwnerFollower.GetIdentityIFollow(samOwnerClient.Identity);
        ClassicAssert.IsTrue(frodoRecordOfFollowingSam.NotificationType == FollowerNotificationType.SelectedChannels);
        ClassicAssert.IsTrue(frodoRecordOfFollowingSam.Channels.Count() == 2, "Frodo should follow 2 of Sam's channels");
        ClassicAssert.IsNotNull(frodoRecordOfFollowingSam.Channels.SingleOrDefault(c => c == channel1Drive.TargetDriveInfo), $"Frodo should have only one record of {nameof(channel1Drive)}");
        ClassicAssert.IsNotNull(frodoRecordOfFollowingSam.Channels.SingleOrDefault(c => c == channel2Drive.TargetDriveInfo), $"Frodo should have only one record of {nameof(channel2Drive)}");

        //sam should have frodo as a follower
        var followingSam = await samOwnerClient.OwnerFollower.GetIdentitiesFollowingMe(string.Empty);
        ClassicAssert.IsTrue(followingSam.Results.Count() == 1, "Sam should have one follower; frodo");
        ClassicAssert.IsTrue(new OdinId(followingSam.Results.Single()) == frodoOwnerClient.Identity.OdinId);

        var samRecordOfFrodoFollowingHim = await samOwnerClient.OwnerFollower.GetFollower(frodoOwnerClient.Identity);
        ClassicAssert.IsTrue(samRecordOfFrodoFollowingHim.NotificationType == FollowerNotificationType.SelectedChannels);
       
        ClassicAssert.IsTrue(samRecordOfFrodoFollowingHim.Channels.Count() == 2, "Frodo should follow 2 of Sam's channels");
        ClassicAssert.IsNotNull(samRecordOfFrodoFollowingHim.Channels.SingleOrDefault(c => c == channel1Drive.TargetDriveInfo), $"Frodo should have only one record of {nameof(channel1Drive)}");
        ClassicAssert.IsNotNull(samRecordOfFrodoFollowingHim.Channels.SingleOrDefault(c => c == channel2Drive.TargetDriveInfo), $"Frodo should have only one record of {nameof(channel2Drive)}");

        var samFollows = await samOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);
        ClassicAssert.IsFalse(samFollows.Results.Any(), "Sam should not be following anyone");

        //All done
        await frodoOwnerClient.OwnerFollower.UnfollowIdentity(samOwnerClient.Identity);
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
    }


    [Test]
    public async Task CanUnfollowIdentity()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        await frodoOwnerClient.OwnerFollower.FollowIdentity(samOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo should follow sam
        var frodoFollows = await frodoOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);
        ClassicAssert.IsTrue(frodoFollows.Results.Count() == 1, "frodo should only follow sam");
        ClassicAssert.IsTrue(new OdinId(frodoFollows.Results.Single()) == samOwnerClient.Identity.OdinId);

        var followingFrodo = await frodoOwnerClient.OwnerFollower.GetIdentitiesFollowingMe(string.Empty);
        ClassicAssert.IsTrue(!followingFrodo.Results.Any());

        //sam should have frodo as follow
        var followingSam = await samOwnerClient.OwnerFollower.GetIdentitiesFollowingMe(string.Empty);
        ClassicAssert.IsTrue(followingSam.Results.Count() == 1, "Sam should have one follower; frodo");
        ClassicAssert.IsTrue(new OdinId(followingSam.Results.Single()) == frodoOwnerClient.Identity.OdinId);

        //
        // Frodo to unfollow sam
        //
        await frodoOwnerClient.OwnerFollower.UnfollowIdentity(samOwnerClient.Identity);

        //Frodo should follow no one
        var updatedFrodoFollows = await frodoOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);
        ClassicAssert.IsTrue(updatedFrodoFollows.Results.All(f => ((OdinId)f) == samOwnerClient.Identity.OdinId), "Frodo should not follow Sam");

        //Sam should have no followers
        var updatedFollowingSam = await samOwnerClient.OwnerFollower.GetIdentitiesFollowingMe(string.Empty);
        ClassicAssert.IsTrue(updatedFollowingSam.Results.All(f => ((OdinId)f) != frodoOwnerClient.Identity.OdinId), "Sam should not follow Frodo");

        //All done
        await frodoOwnerClient.OwnerFollower.UnfollowIdentity(samOwnerClient.Identity);
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task GetIdentities_I_Follow()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        await pippinOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);
        await pippinOwnerClient.OwnerFollower.FollowIdentity(samOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        //
        var pippinFollows = await pippinOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);

        ClassicAssert.IsTrue(pippinFollows.Results.Count() == 2);
        ClassicAssert.IsNotNull(pippinFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == frodoOwnerClient.Identity.OdinId), "Pippin should follow frodo");
        ClassicAssert.IsNotNull(pippinFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == samOwnerClient.Identity.OdinId), "Pippin should follow Sam");

        var frodoFollows = await frodoOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);
        ClassicAssert.IsNull(frodoFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == pippinOwnerClient.Identity.OdinId), "Frodo should not follow Pippin");

        var samFollows = await samOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);
        ClassicAssert.IsNull(samFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == pippinOwnerClient.Identity.OdinId), "Sam should not follow Pippin");

        // All done
        await pippinOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await pippinOwnerClient.OwnerFollower.UnfollowIdentity(samOwnerClient.Identity);
    }

    [Test]
    public async Task GetFollowers_AllNotifications()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        await frodoOwnerClient.OwnerFollower.FollowIdentity(pippinOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);
        await samOwnerClient.OwnerFollower.FollowIdentity(pippinOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        //
        var pippinFollows = await pippinOwnerClient.OwnerFollower.GetIdentitiesFollowingMe(string.Empty);

        ClassicAssert.IsTrue(pippinFollows.Results.Count() == 2);
        ClassicAssert.IsNotNull(pippinFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == frodoOwnerClient.Identity.OdinId), "Pippin should follow frodo");
        ClassicAssert.IsNotNull(pippinFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == samOwnerClient.Identity.OdinId), "Pippin should follow Sam");

        var frodoFollows = await frodoOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);
        ClassicAssert.IsNotNull(frodoFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == pippinOwnerClient.Identity.OdinId), "Frodo should follow Pippin");

        var frodoFollowsPippin = await frodoOwnerClient.OwnerFollower.GetIdentityIFollow(pippinOwnerClient.Identity);
        ClassicAssert.IsNotNull(frodoFollowsPippin);
        ClassicAssert.IsTrue(frodoFollowsPippin.OdinId == pippinOwnerClient.Identity.OdinId);

        var samFollows = await frodoOwnerClient.OwnerFollower.GetIdentitiesIFollow(string.Empty);
        ClassicAssert.IsNotNull(samFollows.Results.SingleOrDefault(ident => ((OdinId)ident) == pippinOwnerClient.Identity.OdinId), "Sam should follow Pippin");

        // All done
        await frodoOwnerClient.OwnerFollower.UnfollowIdentity(pippinOwnerClient.Identity);
        await samOwnerClient.OwnerFollower.UnfollowIdentity(pippinOwnerClient.Identity);
    }

    [Test]
    public async Task FailToFollowYourself()
    {
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        var apiResponse = await pippinOwnerClient.OwnerFollower.FollowIdentity(pippinOwnerClient.Identity, FollowerNotificationType.AllNotifications, null, assertSuccessStatus: false);
        ClassicAssert.IsTrue(apiResponse.StatusCode == HttpStatusCode.BadRequest);

        var pippinAsFollower = await pippinOwnerClient.OwnerFollower.GetFollower(pippinOwnerClient.Identity);
        ClassicAssert.IsNull(pippinAsFollower, "Pippin cannot follow himself");
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