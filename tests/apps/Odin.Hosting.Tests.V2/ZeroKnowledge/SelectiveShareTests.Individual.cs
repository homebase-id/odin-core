#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Core;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Membership.Circles;
using Refit;

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

    }

    [Test]
    public async Task ACircleIsIndividualFromItsCreation()
    {
        // One that became individual could hold several members' keys; one that stopped being individual could take
        // more members, who would get everything shared with its one person.  Refused before any member is re-granted
        var s = await ArrangeAsync();
        await AppCircles.AddMemberAsync(s.App, s.CircleId, Identities.Pippin);
        var toIndividual = await s.Frodo.Admin.GetCircleDefinition(s.CircleId);
        toIndividual.Designation = CircleDesignation.Individual;
        toIndividual.DriveGrants = [];
        var becoming = await s.Frodo.Admin.TryUpdateCircleDefinition(toIndividual);
        Assert.That(becoming.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"becoming individual: {becoming.Error?.Content}");
        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)]);
        await AssertSamSeesAsync(s, fileId, true, "his circle's grant, untouched by the refused update");

        var sams = await IndividualCircleAsync(s, Identities.Sam);
        var toPersonal = await s.Frodo.Admin.GetCircleDefinition(sams);
        toPersonal.Designation = CircleDesignation.Personal;
        toPersonal.DriveGrants = [AppCircles.WriteOn(s.Drive)];
        var leaving = await s.Frodo.Admin.TryUpdateCircleDefinition(toPersonal);
        Assert.That(leaving.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"leaving individual: {leaving.Error?.Content}");
        Assert.That((await s.Frodo.Admin.GetCircleDefinition(sams)).Designation, Is.EqualTo(CircleDesignation.Individual));
    }

    [Test]
    public async Task AnAppMakesPersonalAndIndividualCirclesEachKindByItsOwnSlug()
    {
        var s = await ArrangeAsync();
        var network = s.App.RefitFor<IConnectionNetworkHttpClientApiV2>();

        var vendor = await network.CreateCircle(new CreateAppCircleRequest { Name = "a vendor", Designation = CircleDesignation.Vendor });
        Assert.That(vendor.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"a kind that is the owner's: {vendor.Error?.Content}");

        // A slug's ordinary circle doesn't answer a request for an individual one, nor the other way round
        var slug = new OdinSlug("frodo-" + Guid.NewGuid().ToString("N")[..8]);
        var personal = await network.CreateCircle(new CreateAppCircleRequest
        {
            Name = "Frodo's group", Slug = slug, DriveGrants = [AppCircles.WriteOn(s.Drive)]
        });
        Assert.That(personal.StatusCode, Is.EqualTo(HttpStatusCode.OK), personal.Error?.Content);
        var individual = await network.CreateCircle(new CreateAppCircleRequest
        {
            Name = "Frodo", Slug = slug, Designation = CircleDesignation.Individual
        });
        Assert.That(individual.IsSuccessStatusCode, Is.False, $"the slug names a Personal circle: {individual.StatusCode} {individual.Content}");
        var again = await network.CreateCircle(new CreateAppCircleRequest
        {
            Name = "Frodo's group", Slug = slug, DriveGrants = [AppCircles.WriteOn(s.Drive)]
        });
        Assert.That(again.Content, Is.EqualTo(personal.Content), "the same kind gets the same circle back");
    }

    [Test]
    public async Task AnAppIsntAuthorizedThroughAnIndividualCircle()
    {
        // Its member would get the app's grant through it, and an individual circle grants nothing
        var s = await ArrangeAsync();
        var sams = await IndividualCircleAsync(s, Identities.Sam);

        var refused = Assert.CatchAsync<ApiException>(() => s.Frodo.RefitFor<IRefitOwnerAppRegistration>().UpdateAuthorizedCircles(
            new UpdateAuthorizedCirclesRequest { AppId = s.App.AppId, AuthorizedCircles = [sams], CircleMemberPermissionGrant = s.ReadWriteGrant() }));
        Assert.That(refused!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), refused.Content);
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
        var revoke = await s.Frodo.Connections.RevokeCircle(s.CircleId, s.Sam.Identity);
        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.OK), revoke.Error?.Content);
        await ResetFrodosContextsAsync(s);
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
        await ZkTestFiles.SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", remoteDrive: s.Drive,
            shares: withTheirRow ? [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write, lockedFileKey: LockedFileKey(), circleId: sams)] : null);
        var received = await s.HeaderByUniqueIdAsync(uniqueId);
        var fileId = received.FileId;
        await AssertSamSeesAsync(s, fileId, withTheirRow, "his own file");

        var delete = await s.Sam.Drives.PeerWriter.SendDeleteRequestOverPeer(s.Frodo.Identity, s.Drive,
            received.FileMetadata.GlobalTransitId!.Value);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
        await s.Sam.Sync.DrainOutboxAsync();
        Assert.That((await s.HeaderAsync(fileId)).FileState, Is.EqualTo(withTheirRow ? FileState.Deleted : FileState.Active),
            "his delete");
    }

    /// <summary>The app makes <paramref name="member"/> an individual circle of its own, sets its key and adds them.</summary>
    private static Task<Guid> IndividualCircleAsync(Arranged s, string member) =>
        s.AppCircleAsync(new CreateAppCircleRequest { Name = member, Designation = CircleDesignation.Individual }, member);

    private static async Task<string[]> MembersAsync(Arranged s, Guid circleId)
    {
        var members = await s.App.RefitFor<IConnectionNetworkHttpClientApiV2>().GetCircleMembers(circleId);
        Assert.That(members.StatusCode, Is.EqualTo(HttpStatusCode.OK), members.Error?.Content);
        return members.Content!.Select(m => m.ToString()).ToArray();
    }
}
