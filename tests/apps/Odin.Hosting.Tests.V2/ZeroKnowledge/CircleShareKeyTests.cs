using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
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
        var (app, circleId) = await AppWithItsOwnCircleAsync(owner);
        var keys = app.RefitFor<ICircleShareKeyHttpClient>();

        Assert.That((await keys.GetShareKey(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "a new circle has no key");

        var key = Blob(61, 1);
        Assert.That((await keys.SetShareKey(circleId, new CircleShareKeyResponse { AppKeyLockedShareKey = key })).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await StoredKeyAsync(keys, circleId), Is.EqualTo(key));

        // A second device racing the first loses, and the first key stays
        var second = await keys.SetShareKey(circleId, new CircleShareKeyResponse { AppKeyLockedShareKey = Blob(61, 2) });
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
        var (_, circleId) = await AppWithItsOwnCircleAsync(owner);
        var (other, _) = await AppWithItsOwnCircleAsync(owner);
        var othersKeys = other.RefitFor<ICircleShareKeyHttpClient>();

        Assert.That((await othersKeys.GetShareKey(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await othersKeys.SetShareKey(circleId, new CircleShareKeyResponse { AppKeyLockedShareKey = Blob(61, 1) }))
            .StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await othersKeys.GetUnsealedMembers(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await othersKeys.SetSealedShareKeys(circleId, Sealed(Identities.Sam, 1))).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));

        // An owner circle has no app key to lock with, so not even the owner console sets one
        var ownerCircleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(ownerCircleId, "the owner's", new PermissionSetGrantRequest { Drives = [WriteOn(MeshDrive)] });
        var onOwnerCircle = await owner.RefitFor<ICircleShareKeyHttpClient>()
            .SetShareKey(ownerCircleId, new CircleShareKeyResponse { AppKeyLockedShareKey = Blob(61, 1) });
        Assert.That(onOwnerCircle.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task OnlyAFormatVersionTwoKeyWithinTheColumnBoundIsTaken()
    {
        var owner = await LoginAsOwner();
        var (app, circleId) = await AppWithItsOwnCircleAsync(owner);
        var keys = app.RefitFor<ICircleShareKeyHttpClient>();

        var wrongVersion = Blob(61, 1);
        wrongVersion[0] = 1;
        DriveAsserts.AssertRejected(await keys.SetShareKey(circleId, new CircleShareKeyResponse { AppKeyLockedShareKey = wrongVersion }),
            OdinClientErrorCode.InvalidShareKey);
        DriveAsserts.AssertRejected(await keys.SetShareKey(circleId, new CircleShareKeyResponse { AppKeyLockedShareKey = Blob(513, 1) }),
            OdinClientErrorCode.InvalidShareKey);
        DriveAsserts.AssertRejected(await keys.SetShareKey(circleId, new CircleShareKeyResponse { AppKeyLockedShareKey = [] }),
            OdinClientErrorCode.InvalidShareKey);
        Assert.That((await keys.GetShareKey(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "nothing was stored");
    }

    [Test]
    public async Task MembersGetSealedCopiesAndEachFetchesOnlyTheirOwn()
    {
        var owner = await LoginAsOwner();
        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);
        var (app, circleId) = await AppWithItsOwnCircleAsync(owner);
        var keys = app.RefitFor<ICircleShareKeyHttpClient>();
        await AddAsync(app, circleId, Identities.Sam);

        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.EqualTo(new[] { Identities.Sam }));

        // A copy of a key the circle doesn't have can't be right
        DriveAsserts.AssertRejected(await keys.SetSealedShareKeys(circleId, Sealed(Identities.Sam, 7)),
            OdinClientErrorCode.CircleShareKeyNotSet);

        await keys.SetShareKey(circleId, new CircleShareKeyResponse { AppKeyLockedShareKey = Blob(61, 1) });

        // All or none: Pippin is no member, so Sam's copy in the same batch isn't stored either
        var withPippin = await keys.SetSealedShareKeys(circleId, new SetSealedShareKeysRequest
        {
            Members = [new SealedShareKey { OdinId = Identities.Sam, SealedKey = Blob(200, 7) },
                new SealedShareKey { OdinId = Identities.Pippin, SealedKey = Blob(200, 8) }]
        });
        DriveAsserts.AssertRejected(withPippin, OdinClientErrorCode.NotACircleMember);
        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.EqualTo(new[] { Identities.Sam }));

        var samsCopy = Blob(200, 7);
        Assert.That((await keys.SetSealedShareKeys(circleId, Sealed(Identities.Sam, 7))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.Empty);

        var fetched = await sam.RefitFor<ICircleShareKeyHttpClient>().GetMySealedShareKey(owner.Identity.DomainName, circleId);
        Assert.That(fetched.StatusCode, Is.EqualTo(HttpStatusCode.OK), fetched.Error?.Content);
        Assert.That(fetched.Content!.SealedKey, Is.EqualTo(samsCopy));

        // Pippin is connected to Frodo but not in the circle
        var pippins = await pippin.RefitFor<ICircleShareKeyHttpClient>().GetMySealedShareKey(owner.Identity.DomainName, circleId);
        Assert.That(pippins.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        // Re-sealing (the member's drive key changed) replaces the copy
        await keys.SetSealedShareKeys(circleId, Sealed(Identities.Sam, 9));
        var resealed = await sam.RefitFor<ICircleShareKeyHttpClient>().GetMySealedShareKey(owner.Identity.DomainName, circleId);
        Assert.That(resealed.Content?.SealedKey, Is.EqualTo(Blob(200, 9)), resealed.Error?.Content);
    }

    [Test]
    public async Task AMemberWithoutACopyYetGetsNotFound()
    {
        var owner = await LoginAsOwner();
        var sam = await LoginAsOwner(Identities.Sam);
        var (app, circleId) = await AppWithItsOwnCircleAsync(owner);
        await AddAsync(app, circleId, Identities.Sam);

        var fetched = await sam.RefitFor<ICircleShareKeyHttpClient>().GetMySealedShareKey(owner.Identity.DomainName, circleId);
        Assert.That(fetched.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), fetched.Error?.Content);
    }

    /// <summary>
    /// The writes that rewrite a circle row (disable, enable) and a member's rows (block, unblock: each saves the
    /// connection) keep both keys.  Leaving the circle takes the member's copy; coming back starts without one.
    /// </summary>
    [Test]
    public async Task KeysSurviveRewritesOfTheCircleAndTheConnection()
    {
        var owner = await LoginAsOwner();
        var sam = await LoginAsOwner(Identities.Sam);
        var (app, circleId) = await AppWithItsOwnCircleAsync(owner);
        var keys = app.RefitFor<ICircleShareKeyHttpClient>();
        var network = app.RefitFor<IConnectionNetworkHttpClientApiV2>();
        await AddAsync(app, circleId, Identities.Sam);

        var key = Blob(61, 1);
        await keys.SetShareKey(circleId, new CircleShareKeyResponse { AppKeyLockedShareKey = key });
        await keys.SetSealedShareKeys(circleId, Sealed(Identities.Sam, 7));

        Assert.That((await network.DisableCircle(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await network.EnableCircle(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var ownerNetwork = owner.RefitFor<IConnectionNetworkHttpClientApiV2>();
        Assert.That((await ownerNetwork.Block(new OdinIdRequest { OdinId = Identities.Sam })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await ownerNetwork.Unblock(new OdinIdRequest { OdinId = Identities.Sam })).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That(await StoredKeyAsync(keys, circleId), Is.EqualTo(key), "the circle's key was lost to a rewrite of the circle");
        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.Empty,
            "Sam's copy was lost to a save of the connection");
        var fetched = await sam.RefitFor<ICircleShareKeyHttpClient>().GetMySealedShareKey(owner.Identity.DomainName, circleId);
        Assert.That(fetched.Content?.SealedKey, Is.EqualTo(Blob(200, 7)), fetched.Error?.Content);

        // Leaving takes the copy: Sam is refused, and comes back with none
        Assert.That((await network.RevokeCircle(new RevokeCircleMembershipRequest { CircleId = circleId, OdinId = Identities.Sam }))
            .StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await sam.RefitFor<ICircleShareKeyHttpClient>().GetMySealedShareKey(owner.Identity.DomainName, circleId))
            .StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        await AddAsync(app, circleId, Identities.Sam);
        Assert.That((await keys.GetUnsealedMembers(circleId)).Content, Is.EqualTo(new[] { Identities.Sam }));
        Assert.That(await StoredKeyAsync(keys, circleId), Is.EqualTo(key));
    }

    // -------------------------------------------------------------------------------------------

    /// <summary>An app with no permission keys, its own drive, and a circle of its own granting Write on it.</summary>
    private static async Task<(AppSession App, Guid CircleId)> AppWithItsOwnCircleAsync(OwnerSession owner)
    {
        var appId = Guid.NewGuid();
        var appDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(appDrive, "the app's", allowAnonymousReads: false);
        var app = await AppSession.SetupAsync(owner, appDrive, DrivePermission.All, [], knownAppId: appId);

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "the app's circle", new PermissionSetGrantRequest { Drives = [WriteOn(appDrive)] },
            appId);
        return (app, circleId);
    }

    private static async Task AddAsync(AppSession app, Guid circleId, string identity)
    {
        var add = await app.RefitFor<IConnectionNetworkHttpClientApiV2>()
            .GrantCircle(new AddCircleMembershipRequest { CircleId = circleId, OdinId = identity });
        Assert.That(add.StatusCode, Is.EqualTo(HttpStatusCode.OK), add.Error?.Content);
    }

    /// <summary>The circle's stored key; a missing one fails with the status it got.</summary>
    private static async Task<byte[]> StoredKeyAsync(ICircleShareKeyHttpClient keys, Guid circleId)
    {
        var response = await keys.GetShareKey(circleId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"no share key on circle {circleId}: {response.Error?.Content}");
        return response.Content!.AppKeyLockedShareKey;
    }

    private static DriveGrantRequest WriteOn(TargetDrive drive) =>
        new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Write } };

    /// <summary>A stand-in for a blob the server can't open: format version 2, then <paramref name="fill"/>.</summary>
    private static byte[] Blob(int length, byte fill)
    {
        if (length == 0)
        {
            return [];
        }

        var blob = Enumerable.Repeat(fill, length).ToArray();
        blob[0] = CircleShareKeyService.FormatVersion;
        return blob;
    }

    private static SetSealedShareKeysRequest Sealed(string identity, byte fill) =>
        new() { Members = [new SealedShareKey { OdinId = identity, SealedKey = Blob(200, fill) }] };
}

public interface ICircleShareKeyHttpClient
{
    private const string Root = "/api/v2/connections/circles/{circleId}/share-key";

    [Get(Root)]
    Task<ApiResponse<CircleShareKeyResponse>> GetShareKey(Guid circleId);

    [Put(Root)]
    Task<ApiResponse<HttpContent>> SetShareKey(Guid circleId, [Body] CircleShareKeyResponse request);

    [Get(Root + "/unsealed-members")]
    Task<ApiResponse<List<string>>> GetUnsealedMembers(Guid circleId);

    [Put(Root + "/members")]
    Task<ApiResponse<HttpContent>> SetSealedShareKeys(Guid circleId, [Body] SetSealedShareKeysRequest request);

    [Get(UnifiedApiRouteConstants.PeerRoot + "/{odinId}/circles/{circleId}/share-key")]
    Task<ApiResponse<SealedShareKeyResponse>> GetMySealedShareKey(string odinId, Guid circleId);
}
