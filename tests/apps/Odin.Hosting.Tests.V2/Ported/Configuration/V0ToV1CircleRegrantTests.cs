#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade.Version0tov1;
using Odin.Services.Drives;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// v0 -&gt; v1's circle-grant fix re-mints every circle a connection holds, once per circle, rather than
/// revoking and re-granting per connection through <c>GrantCircleAsync</c>.
/// </summary>
/// <remarks>
/// The old path refused anyone in the Auto Connections circle (since retired, #1809), so an auto-connected
/// contact who also held another circle was skipped.  An auto-connect lands the contact, unreviewed, in Chat.
/// </remarks>
[TestFixture]
public class V0ToV1CircleRegrantTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AnAutoConnectedContact_HasItsOtherCirclesReMinted()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        foreach (var owner in new[] { frodo, sam })
        {
            await owner.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "false");
        }

        var connect = await frodo.Connections.AutoConnectAsync(new ConnectionRequestHeader
        {
            Recipient = sam.Identity,
            Message = "auto-connect",
            ContactData = new ContactRequestData { Name = "test" },
            CircleIds = new List<GuidId>()
        });
        Assert.That(connect.Content?.Outcome, Is.EqualTo(AutoConnectOutcome.Connected), $"detail: {connect.Content?.Detail}");

        var (scope, ctx) = await MigrationContextAsync(sam);
        var storage = scope.Resolve<CircleNetworkStorage>();
        var chatId = BuiltinCircles.ChatCircle.Id;

        var icrBefore = (await storage.GetAsync(frodo.Identity))!;
        var before = icrBefore.PeerKeyStore.CircleGrants;
        Assert.That(icrBefore.ReviewedAt, Is.Null, "precondition: the contact is auto-connected, unreviewed");
        Assert.That(before.Keys, Does.Contain(chatId.Value), "precondition: and in the Chat circle");

        // Change Chat's definition without re-minting its members, so only a re-mint can bring the member's
        // grant up to date.
        var definitions = scope.Resolve<CircleDefinitionService>();
        var chat = await definitions.GetCircleAsync(chatId);
        chat!.DriveGrants = chat.DriveGrants.Append(new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive { Drive = WellKnownAppDrives.ProfileDrive, Permission = DrivePermission.Read }
        }).ToList();
        await definitions.UpdateAsync(chat, skipValidation: true);

        await scope.Resolve<V0ToV1VersionMigrationService>().AutoFixCircleGrantsAsync(ctx, CancellationToken.None);

        var after = (await storage.GetAsync(frodo.Identity))!.PeerKeyStore.CircleGrants;
        var profileGrant = after[chatId.Value].KeyStoreKeyEncryptedDriveGrants
            .SingleOrDefault(g => g.PermissionedDrive.Drive == WellKnownAppDrives.ProfileDrive);

        Assert.That(profileGrant, Is.Not.Null, "the auto-connected contact's Chat grant should have been re-minted");
        Assert.That(profileGrant!.KeyStoreKeyEncryptedStorageKey, Is.Not.Null, "re-minted with the master key, key included");
    }

    /// <remarks>
    /// A connection whose temporary token was sealed to an ECC key this identity has since rotated away
    /// cannot have its token upgraded.  That used to throw out of the re-mint and roll the whole v0 -&gt; v1
    /// upgrade back on every login (seen on bleeding edge); it is left as-is instead, as the KSK upgrade
    /// already does for the same state.
    /// </remarks>
    [Test]
    public async Task ATokenSealedToARotatedKey_DoesNotFailTheReMint()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);

        var (scope, ctx) = await MigrationContextAsync(sam);
        var storage = scope.Resolve<CircleNetworkStorage>();

        var icr = (await storage.GetAsync(frodo.Identity))!;
        Assert.That(icr.PeerKeyStore.CircleGrants, Is.Not.Empty, "precondition: the re-mint has a circle to visit");

        // A token still awaiting its upgrade, sealed to a key whose CRC this identity does not hold.
        var stale = await scope.Resolve<PublicPrivateKeyService>()
            .EccEncryptPayload(PublicPrivateKeyType.OnlineKey, ByteArrayUtil.GetRndByteArray(32));
        stale.EncryptionPublicKeyCrc32 ^= 0xFFFFFFFF;
        icr.TemporaryWeakClientAccessToken = stale;
        icr.EncryptedClientAccessToken = null!;
        await storage.UpsertAsync(icr);

        Assert.DoesNotThrowAsync(() => scope.Resolve<V0ToV1VersionMigrationService>()
            .AutoFixCircleGrantsAsync(ctx, CancellationToken.None));

        var after = (await storage.GetAsync(frodo.Identity))!;
        Assert.That(after.TemporaryWeakClientAccessToken, Is.Not.Null, "the unrecoverable token is left as-is");
    }

    /// <remarks>
    /// A connection with no recoverable Peer Key (its temp weak copy sealed to a key this identity has since
    /// rotated away) cannot be given an app's circle grant.  ReconcileAuthorizedCircles dereferenced the
    /// missing key and threw, rolling the whole v0 -&gt; v1 upgrade back (seen on bleeding edge); it skips the
    /// connection instead, as the circle re-mint already does.
    /// </remarks>
    [Test]
    public async Task AConnectionWithNoRecoverablePeerKey_DoesNotFailTheAppReconcile()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);

        var (scope, ctx) = await MigrationContextAsync(sam);
        var storage = scope.Resolve<CircleNetworkStorage>();

        var icr = (await storage.GetAsync(frodo.Identity))!;
        Assert.That(icr.PeerKeyStore.CircleGrants, Is.Not.Empty, "precondition: frodo is in a circle to reconcile");
        var circleId = icr.PeerKeyStore.CircleGrants.Keys.First();

        icr.PeerKeyStore.MasterKeyEncryptedPeerKey = null!;
        icr.TempWeakKeyStoreKey = null;
        await storage.UpsertAsync(icr);

        // An app authorized for that circle -- Mail, on the identity where this was seen -- so the reconcile
        // has to visit frodo.
        var chat = (await scope.Resolve<IAppRegistrationService>().GetAppRegistration(SystemAppConstants.ChatAppId, ctx))!;
        chat.AuthorizedCircles = [circleId];

        Assert.DoesNotThrowAsync(() => scope.Resolve<CircleNetworkService>()
            .ReconcileAuthorizedCircles(oldAppRegistration: null, chat, ctx));
    }
}
