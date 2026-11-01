using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Circles;
using Odin.Hosting.Controllers;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Network;
using Refit;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Selective Share's circle keys (docs/selective-share-plan.md, PR 2): the app that owns a circle sets its key
/// once and seals a copy for each member; a member fetches only their own copy; and neither key is lost when
/// the circle or a member's connection is written again for other reasons.
/// </summary>
[TestFixture]
public class CircleShareKeyTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin];

    private static readonly TargetDrive MeshDrive = new()
    {
        Alias = Guid.Parse("c1c1e5a1-0000-4000-8000-000000000001"),
        Type = Guid.Parse("c1c1e5a1-0000-4000-8000-000000000002")
    };

    protected override async Task WarmTenantBaselineAsync()
    {
        await base.WarmTenantBaselineAsync();

        var everyone = new List<OwnerSession>();
        foreach (var identity in HostIdentities)
        {
            everyone.Add(await LoginAsOwner(identity));
        }

        await PeerFlow.ConnectAllAsync(everyone, MeshDrive);
    }

    [Test]
    public async Task TheOwningAppSetsTheKeyOnceAndReadsItBack()
    {
        var owner = await LoginAsOwner();
        var (app, circleId, _) = await AppCircles.AppWithItsOwnCircleAsync(owner);
        var keys = app.RefitFor<ICircleShareKeyHttpClient>();

        Assert.That(await StoredKeyAsync(keys, circleId), Is.Null, "a new circle has no key");

        var key = TheKey;
        Assert.That((await keys.SetShareKey(circleId, Key(key))).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await StoredKeyAsync(keys, circleId), Is.EqualTo(key));

        // A second device racing the first loses, and the first key stays
        var second = await keys.SetShareKey(circleId, Key(Blob(61, 2)));
        DriveAsserts.AssertRejected(second, OdinClientErrorCode.CircleShareKeyAlreadySet);
        Assert.That(await StoredKeyAsync(keys, circleId), Is.EqualTo(key));

        // The owner console reaches an app's circle too
        Assert.That(await StoredKeyAsync(owner.RefitFor<ICircleShareKeyHttpClient>(), circleId),
            Is.EqualTo(key));
    }

    [Test]
    public async Task AnotherAppAndAnOwnerCircleAreRefused()
    {
        var owner = await LoginAsOwner();
        var (_, circleId, _) = await AppCircles.AppWithItsOwnCircleAsync(owner);
        var (other, _, _) = await AppCircles.AppWithItsOwnCircleAsync(owner);
        var othersKeys = other.RefitFor<ICircleShareKeyHttpClient>();

        Assert.That((await othersKeys.GetShareKey(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await othersKeys.SetShareKey(circleId, Key(TheKey)))
            .StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await othersKeys.GetUnsealedMembers(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await othersKeys.SetSealedShareKeys(circleId, Sealed((Identities.Sam, 1)))).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));

        // An owner circle has no app key to lock with, so not even the owner console sets one
        var ownerCircleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(ownerCircleId, "the owner's", new PermissionSetGrantRequest { Drives = [AppCircles.WriteOn(MeshDrive)] });
        var ownersKeys = owner.RefitFor<ICircleShareKeyHttpClient>();
        Assert.That((await ownersKeys.SetShareKey(ownerCircleId, Key(TheKey))).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ownersKeys.ResetShareKey(ownerCircleId)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        // Only the owner console is told a circle doesn't exist
        DriveAsserts.AssertRejected(await ownersKeys.ResetShareKey(Guid.NewGuid()), OdinClientErrorCode.CircleNotFound);

        // An app learns nothing about circles it doesn't own: a missing one and an owner circle get the same 403
        var missing = await othersKeys.GetShareKey(Guid.NewGuid());
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), missing.Error?.Content);
        var ownersFromAnApp = await othersKeys.GetShareKey(ownerCircleId);
        Assert.That(ownersFromAnApp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), ownersFromAnApp.Error?.Content);
    }

    [Test]
    public async Task OnlyAFormatVersionTwoKeyWithinTheColumnBoundIsTaken()
    {
        var owner = await LoginAsOwner();
        var (app, circleId, _) = await AppCircles.AppWithItsOwnCircleAsync(owner);
        var keys = app.RefitFor<ICircleShareKeyHttpClient>();

        var wrongVersion = TheKey;
        wrongVersion[0] = 1;
        DriveAsserts.AssertRejected(await keys.SetShareKey(circleId, Key(wrongVersion)),
            OdinClientErrorCode.InvalidShareKey);
        DriveAsserts.AssertRejected(await keys.SetShareKey(circleId, Key(Blob(513, 1))),
            OdinClientErrorCode.InvalidShareKey);
        DriveAsserts.AssertRejected(await keys.SetShareKey(circleId, Key([])),
            OdinClientErrorCode.InvalidShareKey);
        Assert.That(await StoredKeyAsync(keys, circleId), Is.Null, "nothing was stored");

        // Copies must say which key they were sealed from
        DriveAsserts.AssertRejected(await keys.SetSealedShareKeys(circleId, SealedFrom(null!, (Identities.Sam, 7))),
            OdinClientErrorCode.InvalidShareKey);
    }

    [Test]
    public async Task MembersGetSealedCopiesAndEachFetchesOnlyTheirOwn()
    {
        var owner = await LoginAsOwner();
        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);
        var (app, circleId, _) = await AppCircles.AppWithItsOwnCircleAsync(owner);
        var keys = app.RefitFor<ICircleShareKeyHttpClient>();
        await AppCircles.AddMemberAsync(app, circleId, Identities.Sam);

        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.EqualTo(new[] { Identities.Sam }));
        var beforeSealing = await FetchCopyAsync(sam, owner, circleId);
        Assert.That(beforeSealing.StatusCode, Is.EqualTo(HttpStatusCode.OK), beforeSealing.Error?.Content);
        Assert.That(beforeSealing.Content!.SealedKey, Is.Null, "a 404 would mean the route is missing, not that no copy exists");

        // A copy of a key the circle doesn't have can't be right
        DriveAsserts.AssertRejected(await keys.SetSealedShareKeys(circleId, Sealed((Identities.Sam, 7))),
            OdinClientErrorCode.CircleShareKeyNotSet);

        await keys.SetShareKey(circleId, Key(TheKey));

        // All or none: Pippin is no member, so Sam's copy in the same batch isn't stored either
        var withPippin = await keys.SetSealedShareKeys(circleId, Sealed((Identities.Sam, 7), (Identities.Pippin, 8)));
        DriveAsserts.AssertRejected(withPippin, OdinClientErrorCode.NotACircleMember);
        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.EqualTo(new[] { Identities.Sam }));

        Assert.That((await keys.SetSealedShareKeys(circleId, Sealed((Identities.Sam, 7)))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.Empty);

        var fetched = await FetchCopyAsync(sam, owner, circleId);
        Assert.That(fetched.StatusCode, Is.EqualTo(HttpStatusCode.OK), fetched.Error?.Content);
        Assert.That(fetched.Content!.SealedKey, Is.EqualTo(Copy(7)));

        // Pippin is connected to Frodo but not in the circle
        var pippins = await FetchCopyAsync(pippin, owner, circleId);
        Assert.That(pippins.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        // Re-sealing (the member's drive key changed) replaces the copy
        await keys.SetSealedShareKeys(circleId, Sealed((Identities.Sam, 9)));
        var resealed = await FetchCopyAsync(sam, owner, circleId);
        Assert.That(resealed.Content?.SealedKey, Is.EqualTo(Copy(9)), resealed.Error?.Content);
    }

    /// <summary>
    /// A disabled circle grants nothing, so its members can't fetch their copies, even within the hour a peer's
    /// context stays cached; enabling it again gives the same copy back.  Both rewrite the circle row, which
    /// keeps its key.
    /// </summary>
    [Test]
    public async Task ADisabledCircleServesNoCopy()
    {
        var owner = await LoginAsOwner();
        var sam = await LoginAsOwner(Identities.Sam);
        var (app, circleId, keys) = await CircleWithSamsCopyAsync(owner);
        var network = app.RefitFor<IConnectionNetworkHttpClientApiV2>();

        // Fetched once, so Sam's peer context, with the circle in it, is cached on Frodo's server
        Assert.That((await FetchCopyAsync(sam, owner, circleId)).Content?.SealedKey, Is.EqualTo(Copy(7)));

        Assert.That((await network.DisableCircle(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var whileDisabled = await FetchCopyAsync(sam, owner, circleId);
        Assert.That(whileDisabled.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), whileDisabled.Error?.Content);

        Assert.That((await network.EnableCircle(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await AssertSamKeepsCopyAsync(sam, owner, keys, circleId, TheKey, Copy(7), "disable and enable");
    }

    /// <summary>
    /// The developer's way out of a key stored by mistake: the owner console clears the key and every copy, and
    /// the app can set a new one.  The app itself can't.
    /// </summary>
    [Test]
    public async Task TheOwnerConsoleResetsAKeyAndItsCopies()
    {
        var owner = await LoginAsOwner();
        var sam = await LoginAsOwner(Identities.Sam);
        var (_, circleId, keys) = await CircleWithSamsCopyAsync(owner);

        var byTheApp = await keys.ResetShareKey(circleId);
        Assert.That(byTheApp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), byTheApp.Error?.Content);
        Assert.That(await StoredKeyAsync(keys, circleId), Is.EqualTo(TheKey));

        var reset = await owner.RefitFor<ICircleShareKeyHttpClient>().ResetShareKey(circleId);
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.OK), reset.Error?.Content);

        Assert.That(await StoredKeyAsync(keys, circleId), Is.Null, "the key is still there");
        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.EqualTo(new[] { Identities.Sam }));
        var fetched = await FetchCopyAsync(sam, owner, circleId);
        Assert.That(fetched.StatusCode, Is.EqualTo(HttpStatusCode.OK), fetched.Error?.Content);
        Assert.That(fetched.Content?.SealedKey, Is.Null, "Sam still gets the old copy");

        Assert.That((await keys.SetShareKey(circleId, Key(Blob(61, 2)))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await StoredKeyAsync(keys, circleId), Is.EqualTo(Blob(61, 2)));

        // Copies a device sealed from the old key before the reset are refused, and Sam stays listed for sealing
        DriveAsserts.AssertRejected(await keys.SetSealedShareKeys(circleId, Sealed((Identities.Sam, 7))),
            OdinClientErrorCode.CircleShareKeyChanged);
        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.EqualTo(new[] { Identities.Sam }));
        Assert.That((await keys.SetSealedShareKeys(circleId, SealedFrom(Blob(61, 2), (Identities.Sam, 8)))).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>
    /// A connection the owner hasn't reviewed is judged as a stranger wherever content is, so it gets no copy
    /// until the review; the copy sealed before then is served once it lands.
    /// </summary>
    [Test]
    public async Task AnUnreviewedConnectionGetsItsCopyOnlyOnceReviewed()
    {
        var owner = await LoginAsOwner();
        var sam = await LoginAsOwner(Identities.Sam);
        var (_, circleId, _) = await CircleWithSamsCopyAsync(owner);

        var tenant = Host.GetTenantScope(owner.Identity.DomainName);
        await tenant.Resolve<CircleNetworkStorage>().UpdateReviewedAtAsync(sam.Identity, ConnectionStatus.Connected, null);
        await tenant.Resolve<OdinContextCache>().ResetAsync();

        var unreviewed = await FetchCopyAsync(sam, owner, circleId);
        Assert.That(unreviewed.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            $"an unreviewed connection was served: {unreviewed.Content?.SealedKey?.Length} bytes, {unreviewed.Error?.Content}");

        var review = await new V2ConnectionNetworkClient(owner.Identity, owner.Factory).MarkReviewedAsync(sam.Identity);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");

        var reviewed = await FetchCopyAsync(sam, owner, circleId);
        Assert.That(reviewed.Content?.SealedKey, Is.EqualTo(Copy(7)), reviewed.Error?.Content);
    }

    /// <summary>
    /// Saves of the connection keep both keys: block and unblock, which leave the member's rows as they are, and
    /// changing the circle's grants, which rewrites Sam's row with the new grant.  Leaving the circle takes the
    /// member's copy; coming back starts without one.
    /// </summary>
    [Test]
    public async Task KeysSurviveSavesOfTheConnection()
    {
        var owner = await LoginAsOwner();
        var sam = await LoginAsOwner(Identities.Sam);
        var (app, circleId, keys) = await CircleWithSamsCopyAsync(owner);
        var network = app.RefitFor<IConnectionNetworkHttpClientApiV2>();
        var key = TheKey;

        var ownerNetwork = owner.RefitFor<IConnectionNetworkHttpClientApiV2>();
        Assert.That((await ownerNetwork.Block(new OdinIdRequest { OdinId = Identities.Sam })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await ownerNetwork.Unblock(new OdinIdRequest { OdinId = Identities.Sam })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await AssertSamKeepsCopyAsync(sam, owner, keys, circleId, key, Copy(7), "block and unblock");

        // Leaving takes the copy: Sam is refused, and comes back with none
        Assert.That((await network.RevokeCircle(new RevokeCircleMembershipRequest { CircleId = circleId, OdinId = Identities.Sam }))
            .StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await FetchCopyAsync(sam, owner, circleId))
            .StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        await AppCircles.AddMemberAsync(app, circleId, Identities.Sam);
        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.EqualTo(new[] { Identities.Sam }));
        Assert.That(await StoredKeyAsync(keys, circleId), Is.EqualTo(key));

        // Changing what the circle grants rewrites Sam's row, and the copy stays
        Assert.That((await keys.SetSealedShareKeys(circleId, Sealed((Identities.Sam, 8)))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var definition = await owner.Admin.GetCircleDefinition(circleId);
        definition.DriveGrants.Single().PermissionedDrive.Permission = DrivePermission.ReadWrite;
        var update = await owner.Admin.TryUpdateCircleDefinition(definition);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        var samsGrant = (await Host.GetTenantScope(owner.Identity.DomainName).Resolve<CircleMembershipService>()
            .GetCirclesGrantsByDomainAsync(new OdinId(Identities.Sam), DomainType.Identity)).Single(g => g.CircleId == circleId);
        Assert.That(samsGrant.KeyStoreKeyEncryptedDriveGrants.Single().PermissionedDrive.Permission,
            Is.EqualTo(DrivePermission.ReadWrite), "precondition: Sam's row was rewritten with the new grant");
        await AssertSamKeepsCopyAsync(sam, owner, keys, circleId, key, Copy(8), "a change of the circle's grants");
    }

    private static async Task AssertSamKeepsCopyAsync(OwnerSession sam, OwnerSession owner, ICircleShareKeyHttpClient keys,
        Guid circleId, byte[] key, byte[] copy, string after)
    {
        Assert.That(await StoredKeyAsync(keys, circleId), Is.EqualTo(key), $"the circle's key was lost to {after}");
        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.Empty, $"Sam's copy was lost to {after}");
        var fetched = await FetchCopyAsync(sam, owner, circleId);
        Assert.That(fetched.Content?.SealedKey, Is.EqualTo(copy), $"after {after}: {fetched.Error?.Content}");
    }

    // -------------------------------------------------------------------------------------------

    /// <summary>An app's circle with Sam in it, its key <see cref="TheKey"/>, and Sam's copy <c>Copy(7)</c> stored.</summary>
    private static async Task<(AppSession App, Guid CircleId, ICircleShareKeyHttpClient Keys)> CircleWithSamsCopyAsync(
        OwnerSession owner)
    {
        var (app, circleId, _) = await AppCircles.AppWithItsOwnCircleAsync(owner);
        var keys = app.RefitFor<ICircleShareKeyHttpClient>();
        await AppCircles.AddMemberAsync(app, circleId, Identities.Sam);
        var key = await keys.SetShareKey(circleId, Key(TheKey));
        Assert.That(key.StatusCode, Is.EqualTo(HttpStatusCode.OK), key.Error?.Content);
        var copy = await keys.SetSealedShareKeys(circleId, Sealed((Identities.Sam, 7)));
        Assert.That(copy.StatusCode, Is.EqualTo(HttpStatusCode.OK), copy.Error?.Content);
        return (app, circleId, keys);
    }

    /// <summary>The circle's stored key; a missing one fails with the status it got.</summary>
    private static async Task<byte[]> StoredKeyAsync(ICircleShareKeyHttpClient keys, Guid circleId)
    {
        var response = await keys.GetShareKey(circleId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"no share key on circle {circleId}: {response.Error?.Content}");
        return response.Content!.AppKeyLockedShareKey;
    }

    /// <summary>A stand-in for a blob the server can't open: format version 2, then <paramref name="fill"/>.</summary>
    private static byte[] Blob(int length, byte fill)
    {
        var blob = Enumerable.Repeat(fill, length).ToArray();
        blob[0] = CircleShareKeyService.FormatVersion;
        return blob;
    }

    private static CircleShareKeyData Key(byte[] appKeyLockedShareKey) => new() { AppKeyLockedShareKey = appKeyLockedShareKey };

    /// <summary>A member's own copy, fetched through their server from the circle owner's.</summary>
    private static Task<ApiResponse<SealedShareKeyResponse>> FetchCopyAsync(OwnerSession member, OwnerSession owner, Guid circleId) =>
        member.RefitFor<ICircleShareKeyHttpClient>().GetMySealedShareKey(owner.Identity.DomainName, circleId);

    /// <summary>A stand-in for a member's sealed copy.</summary>
    private static byte[] Copy(byte fill) => Blob(200, fill);

    /// <summary>The key every test sets first; a new array each time, so no test can change another's.</summary>
    private static byte[] TheKey => Blob(61, 1);

    /// <summary>Copies sealed from the key every test sets first, <see cref="TheKey"/>.</summary>
    private static SetSealedShareKeysRequest Sealed(params (string Identity, byte Fill)[] members) => SealedFrom(TheKey, members);

    private static SetSealedShareKeysRequest SealedFrom(byte[] key, params (string Identity, byte Fill)[] members) => new()
    {
        SealedFrom = key,
        Members = members.Select(m => new SealedShareKey { OdinId = m.Identity, SealedKey = Copy(m.Fill) }).ToList()
    };
}

public interface ICircleShareKeyHttpClient
{
    // CircleShareKey and PeerCircleShareKey without their :guid route constraint, which Refit refuses
    private const string Root = UnifiedApiRouteConstants.Connections + "/circles/{circleId}/share-key";

    [Get(Root)]
    Task<ApiResponse<CircleShareKeyData>> GetShareKey(Guid circleId);

    [Put(Root)]
    Task<ApiResponse<HttpContent>> SetShareKey(Guid circleId, [Body] CircleShareKeyData request);

    [Delete(Root)]
    Task<ApiResponse<HttpContent>> ResetShareKey(Guid circleId);

    [Get(Root + "/unsealed-members")]
    Task<ApiResponse<List<string>>> GetUnsealedMembers(Guid circleId);

    [Put(Root + "/members")]
    Task<ApiResponse<HttpContent>> SetSealedShareKeys(Guid circleId, [Body] SetSealedShareKeysRequest request);

    [Get(UnifiedApiRouteConstants.PeerByOdinId + "/circles/{circleId}/share-key")]
    Task<ApiResponse<SealedShareKeyResponse>> GetMySealedShareKey(string odinId, Guid circleId);
}
