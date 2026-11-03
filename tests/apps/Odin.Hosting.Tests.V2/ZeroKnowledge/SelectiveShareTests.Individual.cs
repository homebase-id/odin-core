#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Circles;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Shares;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Sharing with one person (file-access.md, "Sharing with one person"): an individual circle has one member and grants
/// nothing; its share rows narrow what the member's other circles grant, and a member's new file can carry a row for
/// their own, so they keep it.
/// </summary>
public partial class SelectiveShareTests
{
    [Test]
    public async Task AnIndividualCircleGrantsNothingAndEnrolsNoOne()
    {
        var s = await ArrangeAsync();

        var granting = Assert.ThrowsAsync<InvalidOperationException>(() => s.Frodo.Admin.CreateCircle(Guid.NewGuid(), "Sam",
            s.ReadWriteGrant(), s.App.AppId, designation: CircleDesignation.Individual));
        Assert.That(granting!.Message, Does.Contain("grants nothing"), "a drive grant");

        var enrolling = Assert.ThrowsAsync<InvalidOperationException>(() => s.Frodo.Admin.CreateCircle(Guid.NewGuid(), "Sam",
            new PermissionSetGrantRequest(), s.App.AppId, CircleGrantOn.Connect, CircleDesignation.Individual));
        Assert.That(enrolling!.Message, Does.Contain("grants nothing"), "enrolling on connect");

        var circleId = Guid.NewGuid();
        await s.Frodo.Admin.CreateCircle(circleId, "Sam", new PermissionSetGrantRequest(), s.App.AppId,
            designation: CircleDesignation.Individual);
        Assert.That((await s.Frodo.Admin.GetCircleDefinition(circleId)).Designation, Is.EqualTo(CircleDesignation.Individual));
    }

    [Test]
    public async Task AnIndividualCircleTakesOneMember()
    {
        var s = await ArrangeAsync();
        var circleId = await IndividualCircleAsync(s, Identities.Sam);

        var second = await s.App.RefitFor<IConnectionNetworkHttpClientApiV2>()
            .GrantCircle(new AddCircleMembershipRequest { CircleId = circleId, OdinId = Identities.Pippin });
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"a second member: {second.Error?.Content}");
        Assert.That(await MembersAsync(s, circleId), Is.EqualTo(new[] { Identities.Sam }), "still Sam alone");

        // Nor does a circle of two become one
        var pair = await s.CircleWithKeyAsync(DrivePermission.Read);
        await AppCircles.AddMemberAsync(s.App, pair, Identities.Sam);
        await AppCircles.AddMemberAsync(s.App, pair, Identities.Pippin);
        var definition = await s.Frodo.Admin.GetCircleDefinition(pair);
        definition.Designation = CircleDesignation.Individual;
        definition.DriveGrants = [];
        var update = await s.Frodo.Admin.TryUpdateCircleDefinition(definition);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"two members: {update.Error?.Content}");
    }

    [Test]
    public async Task AShareWithOnePersonGoesByTheirOtherCircles()
    {
        var s = await ArrangeAsync();
        var sams = await IndividualCircleAsync(s, Identities.Sam);
        var pippins = await IndividualCircleAsync(s, Identities.Pippin);
        var fileId = await s.UploadAsync(SecurityGroupType.Owner);

        // Pippin is in no other circle of the app, so nothing grants him the drive
        var toPippin = await s.Shares.WriteShares(s.Drive.Alias, s.Request(fileId, DrivePermission.Read, pippins));
        Assert.That(toPippin.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"Pippin: {toPippin.Error?.Content}");

        var toSam = await s.Shares.WriteShares(s.Drive.Alias, s.Request(fileId, DrivePermission.Read | DrivePermission.Write, sams));
        Assert.That(toSam.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Sam, whose app circle grants the drive: {toSam.Error?.Content}");
        await AssertSamSeesAsync(s, fileId, true, "shared with him alone");

        // Out of the circle that grants him the drive, his own share gives him nothing, and stays as it was
        var revoke = await s.App.RefitFor<IConnectionNetworkHttpClientApiV2>()
            .RevokeCircle(new RevokeCircleMembershipRequest { CircleId = s.CircleId, OdinId = Identities.Sam });
        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.OK), revoke.Error?.Content);
        await Host.GetTenantScope(s.Frodo.Identity.DomainName).Resolve<OdinContextCache>().ResetAsync();
        await AssertSeesAsync(s.Sam, s, fileId, false, "out of the app's circle", hasDriveAccess: false);
        Assert.That((await s.SharesAsync(fileId)).Single().CircleId, Is.EqualTo(sams), "the share is still there");
        Assert.That(await MembersAsync(s, sams), Is.EqualTo(new[] { Identities.Sam }), "and so is he, in his own circle");
    }

    [TestCase(true, TestName = "AMemberKeepsTheirNewFileThroughTheirIndividualCircle")]
    [TestCase(false, TestName = "AMembersNewFileWithoutTheirRowIsTheOwnersAlone")]
    public async Task AMemberKeepsTheirNewFile(bool withTheirRow)
    {
        var s = await ArrangeAsync();
        var sams = await IndividualCircleAsync(s, Identities.Sam);

        var uniqueId = Guid.NewGuid();
        await ZkTestFiles.SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", null, s.Drive, null,
            withTheirRow ? [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write, lockedFileKey: LockedFileKey(), circleId: sams)] : null);
        var received = await s.Frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, s.Drive.Alias);
        Assert.That(received.StatusCode, Is.EqualTo(HttpStatusCode.OK), received.Error?.Content);
        var fileId = received.Content!.FileId;
        await AssertSamSeesAsync(s, fileId, withTheirRow, "his own file");

        var delete = await s.Sam.Drives.PeerWriter.SendDeleteRequestOverPeer(s.Frodo.Identity, s.Drive,
            received.Content.FileMetadata.GlobalTransitId!.Value);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
        await s.Sam.Sync.DrainOutboxAsync();
        Assert.That((await s.HeaderAsync(fileId)).FileState, Is.EqualTo(withTheirRow ? FileState.Deleted : FileState.Active),
            "his delete");
    }

    /// <summary>The app makes <paramref name="member"/> an individual circle of its own, sets its key and adds them.</summary>
    private static async Task<Guid> IndividualCircleAsync(Arranged s, string member)
    {
        var create = await s.App.RefitFor<IConnectionNetworkHttpClientApiV2>().CreateCircle(new CreateAppCircleRequest
        {
            Name = member, Designation = CircleDesignation.Individual
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), create.Error?.Content);
        var circleId = create.Content;
        await s.SetShareKeyAsync(circleId);
        await AppCircles.AddMemberAsync(s.App, circleId, member);
        return circleId;
    }

    private static async Task<string[]> MembersAsync(Arranged s, Guid circleId)
    {
        var members = await s.App.RefitFor<IConnectionNetworkHttpClientApiV2>().GetCircleMembers(circleId);
        Assert.That(members.StatusCode, Is.EqualTo(HttpStatusCode.OK), members.Error?.Content);
        return members.Content!.Select(m => m.ToString()).ToArray();
    }
}
