#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade.Version0tov1;
using Odin.Services.Drives;
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
}
