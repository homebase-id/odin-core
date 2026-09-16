using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Follower;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Permissions;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Follower;

/// <summary>
/// Port of <c>OwnerApi/DataSubscription/Follower/OwnerFollowerTests</c>,
/// <c>AppAPI/Follower/AppFollowerTests</c> and <c>_Universal/Follower/FollowerTests</c> — the
/// follow / unfollow / who-follows-whom surface (<c>/followers/*</c>), read back over both the owner
/// and the app V1 route families.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three fixtures collapsed into one.</b> <c>AppAPI/Follower/AppFollowerTests</c> and
/// <c>_Universal/Follower/FollowerTests</c> were <i>byte-identical</i> apart from the namespace, the
/// class name, and one unused <c>using System.Net;</c> — 300 lines of literal copy, 5 duplicate
/// test cases. <c>OwnerFollowerTests</c> is the same five scenarios with every read issued through
/// the owner client instead of an app client, plus one owner-only extra
/// (<see cref="FailToFollowYourself"/>). So the caller is the only real axis, and it is
/// <see cref="ReaderCases"/> here.
/// </para>
/// <para>
/// <b>Provenance.</b> The <c>Owner</c> row of each matrix test is that test from
/// <c>OwnerFollowerTests</c>; the <c>App[...]</c> row is that test from <c>AppFollowerTests</c> (and
/// hence also from <c>_Universal/FollowerTests</c>, which was its clone).
/// <see cref="FailToFollowYourself"/> came only from <c>OwnerFollowerTests</c> and stays a plain
/// <c>[Test]</c>. 16 V1 cases → 11 cases here; the 5 that disappear are exactly the duplicate file.
/// No scenario is lost.
/// </para>
/// <para>
/// <b>The endpoints really are both exercised.</b> <c>ITestFollowerOwnerClient</c>,
/// <c>ITestFollowerAppClient</c> and <c>IRefitUniversalFollowerClient</c> declare the same six
/// relative routes; the universal one is resolved against the caller's own V1 base by
/// <c>InProcessApiClientFactory</c>, so the Owner row hits <c>/api/owner/v1/followers/*</c> and the
/// App row <c>/api/apps/v1/followers/*</c> — the same split the two originals had.
/// </para>
/// <para>
/// <b>Carried defects — behaviour left exactly as found (see the individual tests):</b>
/// <list type="number">
/// <item><see cref="CanUnfollowIdentity"/>: the last assertion reads Sam's follower list in the
/// owner original but <i>Frodo's</i> in the app original, under the same "Sam should not follow
/// Frodo" message. That makes the app variant vacuous. Preserved per row via the
/// <c>finalCheckReadsSams</c> column of <see cref="UnfollowCases"/> so neither original's coverage
/// changes.</item>
/// <item><see cref="CanUnfollowIdentity"/>: "Frodo should not follow Sam" is asserted as
/// <c>Results.All(f => f == sam)</c> — which is vacuously true on the empty list it actually gets,
/// and would <i>also</i> pass if Frodo still followed Sam. Present in all three originals.</item>
/// <item><see cref="GetFollowers_AllNotifications"/>: the variable named <c>samFollows</c>, asserted
/// with "Sam should follow Pippin", is read off <i>Frodo's</i> client, not Sam's — so Sam's side is
/// never checked. Present in all three originals.</item>
/// </list>
/// </para>
/// <para>
/// <b>Arrange differences, all checked as inert.</b>
/// (a) <c>SetupCallerWithOwner</c> ordering <i>is</i> in play: the app originals issued the owner's
/// follow before registering the app, and here the reader (and its app) is built first. App
/// registration neither reads nor snapshots follower state — the app's ODIN context is rebuilt per
/// request — so the reads are unaffected.
/// (b) Both rows now get a drive created per identity (<c>DriveSpec.Secured()</c>, matching the app
/// original's non-anonymous "Chat Drive 1"); the owner original created none. Nothing here reads a
/// drive list, and the drive is neither a channel type nor subscription-enabled, so it cannot be
/// followed or appear in a channel query. The name differs ("Test Drive" vs "Chat Drive 1") and is
/// never read.
/// (c) <c>UniversalFollowerApiClient.FollowIdentity</c> coerces a null <c>Channels</c> to <c>[]</c>
/// where the V1 owner client sent null. Inert: for <c>AllNotifications</c>, <c>Channels</c> is
/// copied into the perimeter request and then ignored by both
/// <c>FollowerService.FollowAsync</c> and <c>FollowerPerimeterService.AcceptFollowerAsync</c>, and
/// the <c>Channels</c> the tests read back is rebuilt from the DB rows, not from the request.
/// (d) Trailing unfollow pairs were pure state restoration and are dropped — per-test reset owns
/// that. The unfollow inside <see cref="CanUnfollowIdentity"/> is the system under test and stays.
/// </para>
/// <para>
/// <b>Status codes.</b> The V1 clients asserted <c>IsSuccessStatusCode</c>, which hid two 204s:
/// <c>follow</c> and <c>unfollow</c> return <c>NoContent</c> by construction
/// (<c>FollowerControllerBase</c>), and <c>GET /followers/follower</c> returns <c>NoContent</c>
/// rather than <c>200</c>-with-a-null-body when there is no such follower. Both are named exactly
/// here.
/// </para>
/// </remarks>
[TestFixture]
public class FollowerTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin];

    /// <summary>
    /// The one axis these three fixtures actually differed on: which client family the follower reads
    /// go out over. Owner row ← <c>OwnerFollowerTests</c>; App row ← <c>AppFollowerTests</c> /
    /// <c>_Universal/FollowerTests</c>.
    /// </summary>
    public static IEnumerable<object[]> ReaderCases()
    {
        yield return [OwnerReader()];
        yield return [AppReader()];
    }

    /// <summary>
    /// <see cref="ReaderCases"/> plus the one place the two originals genuinely diverged: whose
    /// follower list the final assertion of <see cref="CanUnfollowIdentity"/> reads. Carried defect
    /// (1) in the class remarks — the app original read Frodo's where the owner original read Sam's.
    /// </summary>
    public static IEnumerable<object[]> UnfollowCases()
    {
        yield return [OwnerReader(), true];
        yield return [AppReader(), false];
    }

    private static CallerSpec OwnerReader() => CallerSpec.Owner(DriveSpec.Secured());

    private static CallerSpec AppReader() => CallerSpec.App(DriveSpec.Secured(), DrivePermission.All,
        new[] { PermissionKeys.ReadMyFollowers, PermissionKeys.ReadWhoIFollow });

    [Test, TestCaseSource(nameof(ReaderCases))]
    public async Task CanFollowIdentity_AllNotifications(CallerSpec spec)
    {
        //
        // Setup followers
        //
        var frodo = await SetupReaderAsync(spec, Identities.Frodo);
        var sam = await SetupReaderAsync(spec, Identities.Sam);

        await FollowAsync(frodo.Owner, sam.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo should follow Sam
        var frodoFollows = await IdentitiesIFollowAsync(frodo);
        Assert.That(frodoFollows.Results.Count(), Is.EqualTo(1), "frodo should only follow sam");
        Assert.That((OdinId)frodoFollows.Results.Single(), Is.EqualTo(sam.Identity));

        var followingFrodo = await IdentitiesFollowingMeAsync(frodo);
        Assert.That(followingFrodo.Results, Is.Empty);

        var frodoRecordOfFollowingSam = await IdentityIFollowRecordAsync(frodo, sam.Identity);
        Assert.That(frodoRecordOfFollowingSam.NotificationType, Is.EqualTo(FollowerNotificationType.AllNotifications));
        Assert.That(frodoRecordOfFollowingSam.Channels, Is.Null,
            "there should be no channels when notification type is all notifications");

        //sam should have frodo
        var followingSam = await IdentitiesFollowingMeAsync(sam);
        Assert.That(followingSam.Results.Count(), Is.EqualTo(1), "Sam should have one follower; frodo");
        Assert.That((OdinId)followingSam.Results.Single(), Is.EqualTo(frodo.Identity));

        var samRecordOfFrodoFollowingHim = await FollowerRecordAsync(sam, frodo.Identity);
        Assert.That(samRecordOfFrodoFollowingHim.NotificationType, Is.EqualTo(FollowerNotificationType.AllNotifications));
        Assert.That(samRecordOfFrodoFollowingHim.Channels, Is.Null,
            "there should be no channels when notification type is all notifications");

        var samFollows = await IdentitiesIFollowAsync(sam);
        Assert.That(samFollows.Results, Is.Empty, "Sam should not be following anyone");
    }

    [Test, TestCaseSource(nameof(ReaderCases))]
    public async Task CanFollowIdentity_SelectedChannels(CallerSpec spec)
    {
        var frodo = await SetupReaderAsync(spec, Identities.Frodo);
        var sam = await SetupReaderAsync(spec, Identities.Sam);

        //create some channels for Sam
        var channel1Drive = await CreateChannelDriveAsync(sam.Owner, "Channel 1");
        var channel2Drive = await CreateChannelDriveAsync(sam.Owner, "Channel 2");

        //Frodo will follow Sam
        await FollowAsync(frodo.Owner, sam.Identity, FollowerNotificationType.SelectedChannels,
            new List<TargetDrive>() { channel1Drive, channel2Drive });

        // Frodo should follow Sam
        var frodoFollows = await IdentitiesIFollowAsync(frodo);
        Assert.That(frodoFollows.Results.Count(), Is.EqualTo(1), "frodo should only follow sam");
        Assert.That((OdinId)frodoFollows.Results.Single(), Is.EqualTo(sam.Identity));

        var followingFrodo = await IdentitiesFollowingMeAsync(frodo);
        Assert.That(followingFrodo.Results, Is.Empty, "Frodo should have no followers");

        var frodoRecordOfFollowingSam = await IdentityIFollowRecordAsync(frodo, sam.Identity);
        Assert.That(frodoRecordOfFollowingSam.NotificationType, Is.EqualTo(FollowerNotificationType.SelectedChannels));
        Assert.That(frodoRecordOfFollowingSam.Channels.Count(), Is.EqualTo(2), "Frodo should follow 2 of Sam's channels");
        Assert.That(frodoRecordOfFollowingSam.Channels, Has.Exactly(1).Matches<TargetDrive>(c => c == channel1Drive),
            $"Frodo should have only one record of {nameof(channel1Drive)}");
        Assert.That(frodoRecordOfFollowingSam.Channels, Has.Exactly(1).Matches<TargetDrive>(c => c == channel2Drive),
            $"Frodo should have only one record of {nameof(channel2Drive)}");

        //sam should have frodo as a follower
        var followingSam = await IdentitiesFollowingMeAsync(sam);
        Assert.That(followingSam.Results.Count(), Is.EqualTo(1), "Sam should have one follower; frodo");
        Assert.That((OdinId)followingSam.Results.Single(), Is.EqualTo(frodo.Identity));

        var samRecordOfFrodoFollowingHim = await FollowerRecordAsync(sam, frodo.Identity);
        Assert.That(samRecordOfFrodoFollowingHim.NotificationType, Is.EqualTo(FollowerNotificationType.SelectedChannels));

        Assert.That(samRecordOfFrodoFollowingHim.Channels.Count(), Is.EqualTo(2), "Frodo should follow 2 of Sam's channels");
        Assert.That(samRecordOfFrodoFollowingHim.Channels, Has.Exactly(1).Matches<TargetDrive>(c => c == channel1Drive),
            $"Frodo should have only one record of {nameof(channel1Drive)}");
        Assert.That(samRecordOfFrodoFollowingHim.Channels, Has.Exactly(1).Matches<TargetDrive>(c => c == channel2Drive),
            $"Frodo should have only one record of {nameof(channel2Drive)}");

        var samFollows = await IdentitiesIFollowAsync(sam);
        Assert.That(samFollows.Results, Is.Empty, "Sam should not be following anyone");
    }

    /// <param name="finalCheckReadsSams">
    /// Carried defect (1): whether the closing "Sam should not follow Frodo" assertion reads Sam's
    /// follower list (the owner original) or Frodo's (the app original, where it is vacuous).
    /// </param>
    [Test, TestCaseSource(nameof(UnfollowCases))]
    public async Task CanUnfollowIdentity(CallerSpec spec, bool finalCheckReadsSams)
    {
        var frodo = await SetupReaderAsync(spec, Identities.Frodo);
        var sam = await SetupReaderAsync(spec, Identities.Sam);

        await FollowAsync(frodo.Owner, sam.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo should follow sam
        var frodoFollows = await IdentitiesIFollowAsync(frodo);
        Assert.That(frodoFollows.Results.Count(), Is.EqualTo(1), "frodo should only follow sam");
        Assert.That((OdinId)frodoFollows.Results.Single(), Is.EqualTo(sam.Identity));

        var followingFrodo = await IdentitiesFollowingMeAsync(frodo);
        Assert.That(followingFrodo.Results, Is.Empty);

        //sam should have frodo as follow
        var followingSam = await IdentitiesFollowingMeAsync(sam);
        Assert.That(followingSam.Results.Count(), Is.EqualTo(1), "Sam should have one follower; frodo");
        Assert.That((OdinId)followingSam.Results.Single(), Is.EqualTo(frodo.Identity));

        //
        // Frodo to unfollow sam
        //
        await UnfollowAsync(frodo.Owner, sam.Identity);

        // Frodo should follow no one.
        // Carried defect (2): the predicate below says the opposite of the message, and passes
        // vacuously on the empty list it gets. Left exactly as all three originals had it.
        var updatedFrodoFollows = await IdentitiesIFollowAsync(frodo);
        Assert.That(updatedFrodoFollows.Results, Is.All.Matches<string>(f => ((OdinId)f) == sam.Identity),
            "Frodo should not follow Sam");

        //Sam should have no followers
        var updatedFollowingSam = await IdentitiesFollowingMeAsync(finalCheckReadsSams ? sam : frodo);
        Assert.That(updatedFollowingSam.Results, Has.None.Matches<string>(f => ((OdinId)f) == frodo.Identity),
            "Sam should not follow Frodo");
    }

    [Test, TestCaseSource(nameof(ReaderCases))]
    public async Task GetIdentities_I_Follow(CallerSpec spec)
    {
        var frodo = await SetupReaderAsync(spec, Identities.Frodo);
        var sam = await SetupReaderAsync(spec, Identities.Sam);
        var pippin = await SetupReaderAsync(spec, Identities.Pippin);

        await FollowAsync(pippin.Owner, frodo.Identity, FollowerNotificationType.AllNotifications, null);
        await FollowAsync(pippin.Owner, sam.Identity, FollowerNotificationType.AllNotifications, null);

        //
        var pippinFollows = await IdentitiesIFollowAsync(pippin);

        Assert.That(pippinFollows.Results.Count(), Is.EqualTo(2));
        Assert.That(pippinFollows.Results, Has.Exactly(1).Matches<string>(ident => ((OdinId)ident) == frodo.Identity),
            "Pippin should follow frodo");
        Assert.That(pippinFollows.Results, Has.Exactly(1).Matches<string>(ident => ((OdinId)ident) == sam.Identity),
            "Pippin should follow Sam");

        var frodoFollows = await IdentitiesIFollowAsync(frodo);
        Assert.That(frodoFollows.Results, Has.None.Matches<string>(ident => ((OdinId)ident) == pippin.Identity),
            "Frodo should not follow Pippin");

        var samFollows = await IdentitiesIFollowAsync(sam);
        Assert.That(samFollows.Results, Has.None.Matches<string>(ident => ((OdinId)ident) == pippin.Identity),
            "Sam should not follow Pippin");
    }

    [Test, TestCaseSource(nameof(ReaderCases))]
    public async Task GetFollowers_AllNotifications(CallerSpec spec)
    {
        var frodo = await SetupReaderAsync(spec, Identities.Frodo);
        var pippin = await SetupReaderAsync(spec, Identities.Pippin);

        // Sam only ever acts as owner here -- the app original built no app client for him.
        var samOwner = await LoginAsOwner(Identities.Sam);

        await FollowAsync(frodo.Owner, pippin.Identity, FollowerNotificationType.AllNotifications, null);
        await FollowAsync(samOwner, pippin.Identity, FollowerNotificationType.AllNotifications, null);

        //
        var pippinFollows = await IdentitiesFollowingMeAsync(pippin);

        Assert.That(pippinFollows.Results.Count(), Is.EqualTo(2));
        Assert.That(pippinFollows.Results, Has.Exactly(1).Matches<string>(ident => ((OdinId)ident) == frodo.Identity),
            "Pippin should follow frodo");
        Assert.That(pippinFollows.Results, Has.Exactly(1).Matches<string>(ident => ((OdinId)ident) == samOwner.Identity),
            "Pippin should follow Sam");

        var frodoFollows = await IdentitiesIFollowAsync(frodo);
        Assert.That(frodoFollows.Results, Has.Exactly(1).Matches<string>(ident => ((OdinId)ident) == pippin.Identity),
            "Frodo should follow Pippin");

        var frodoFollowsPippin = await IdentityIFollowRecordAsync(frodo, pippin.Identity);
        Assert.That(frodoFollowsPippin, Is.Not.Null);
        Assert.That(frodoFollowsPippin.OdinId, Is.EqualTo(pippin.Identity));

        // Carried defect (3): named samFollows and asserted "Sam should follow Pippin", but read off
        // Frodo's client -- so Sam's who-I-follow list is never checked. All three originals did this.
        var samFollows = await IdentitiesIFollowAsync(frodo);
        Assert.That(samFollows.Results, Has.Exactly(1).Matches<string>(ident => ((OdinId)ident) == pippin.Identity),
            "Sam should follow Pippin");
    }

    /// <summary>From <c>OwnerFollowerTests</c> only — no app counterpart, so no caller matrix.</summary>
    [Test]
    public async Task FailToFollowYourself()
    {
        var pippinOwnerClient = await LoginAsOwner(Identities.Pippin);

        var apiResponse = await pippinOwnerClient.V1.Follower.FollowIdentity(pippinOwnerClient.Identity,
            FollowerNotificationType.AllNotifications, null);
        Assert.That(apiResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // 204, not 200-with-null-body -- see FollowerRecordAsync.
        var pippinAsFollower = await FollowerRecordAsync(
            new Reader(pippinOwnerClient, pippinOwnerClient.V1.Follower), pippinOwnerClient.Identity,
            HttpStatusCode.NoContent);
        Assert.That(pippinAsFollower, Is.Null, "Pippin cannot follow himself");
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

    // -------------------------------------------------------------------------------------------
    // Arrange
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// One identity's pair of handles: the owner session that issues follows/unfollows (an owner
    /// endpoint in every original, whichever client did the reading) and the follower client the
    /// matrix row reads through — the owner's own for the Owner row, a freshly registered app's for
    /// the App row.
    /// </summary>
    private sealed record Reader(OwnerSession Owner, UniversalFollowerApiClient Follower)
    {
        public OdinId Identity => Owner.Identity;
    }

    private async Task<Reader> SetupReaderAsync(CallerSpec spec, string identity)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec, identity);
        return new Reader(owner, caller.V1.Follower);
    }

    /// <summary>
    /// A channel drive that can be followed: channel drive type, anonymous-readable (so the followed
    /// identity's perimeter read-access check passes) and subscription-enabled.
    /// </summary>
    private static async Task<TargetDrive> CreateChannelDriveAsync(OwnerSession owner, string name)
    {
        var drive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await owner.Admin.CreateDrive(drive, name, allowAnonymousReads: true, ownerOnly: false, allowSubscriptions: true);
        return drive;
    }

    private static async Task FollowAsync(OwnerSession owner, OdinId identity, FollowerNotificationType notificationType,
        List<TargetDrive> channels)
    {
        var response = await owner.V1.Follower.FollowIdentity(identity, notificationType, channels);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), $"Failed to follow identity: [{identity}]");
    }

    private static async Task UnfollowAsync(OwnerSession owner, OdinId identity)
    {
        var response = await owner.V1.Follower.UnfollowIdentity(identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            $"Failed to unfollow identity: [{identity}]");
    }

    // -------------------------------------------------------------------------------------------
    // Reads. The V1 clients folded these success/non-null assertions in; they stay here so the
    // matrix row's client is the only thing that varies.
    // -------------------------------------------------------------------------------------------

    private static async Task<CursoredResult<string>> IdentitiesIFollowAsync(Reader reader)
    {
        var response = await reader.Follower.GetIdentitiesIFollow(string.Empty);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        return response.Content;
    }

    private static async Task<CursoredResult<string>> IdentitiesFollowingMeAsync(Reader reader)
    {
        var response = await reader.Follower.GetIdentitiesFollowingMe(string.Empty);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        return response.Content;
    }

    /// <param name="expected">
    /// <c>OK</c> where a record is expected. <c>GET /followers/follower</c> answers <c>204</c>, not
    /// <c>200</c> with a null body, when the identity does not follow the caller — the V1 client's
    /// <c>IsSuccessStatusCode</c> check covered both, so <see cref="FailToFollowYourself"/> names it.
    /// </param>
    private static async Task<FollowerDefinition> FollowerRecordAsync(Reader reader, OdinId identity,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await reader.Follower.GetFollower(identity);
        Assert.That(response.StatusCode, Is.EqualTo(expected));
        return response.Content;
    }

    private static async Task<FollowerDefinition> IdentityIFollowRecordAsync(Reader reader, OdinId identity)
    {
        var response = await reader.Follower.GetIdentityIFollow(identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content;
    }
}
