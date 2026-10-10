#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Configuration.VersionUpgrade.Version19tov20;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Covers the v19 -&gt; v20 pass that deletes the Confirmed Connections and Auto-connected system circles.
/// </summary>
/// <remarks>
/// A fresh identity no longer has them, so each test puts them back the way an older identity holds
/// them: the definitions, Chat authorizing both, and a connection holding both as circle and app grants.
/// </remarks>
[TestFixture]
public class SystemCircleRemovalMigrationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task TheSystemCircles_AreRemovedFromEverywhere()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (scope, ctx) = await MigrationContextAsync(frodo);
        await SeedSystemCirclesAsync(scope, ctx, sam);

        var migration = scope.Resolve<V19ToV20VersionMigrationService>();
        Assert.ThrowsAsync<OdinSystemException>(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None),
            "validation must not pass while the system circles exist");

        await migration.DeleteSystemCirclesAsync(ctx, CancellationToken.None);

        var definitions = scope.Resolve<CircleDefinitionService>();
        var db = scope.Resolve<IdentityDatabase>();
        foreach (var id in V19ToV20VersionMigrationService.RetiredSystemCircleIds)
        {
            Assert.That(await definitions.GetCircleAsync(id), Is.Null, "the definition should be gone");
            Assert.That(await db.CircleMemberCached.GetCircleMembersAsync(id), Is.Empty, "and its membership rows");
        }

        var chat = await scope.Resolve<IAppRegistrationService>().GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        Assert.That(chat!.AuthorizedCircles, Has.None.Matches<Guid>(V19ToV20VersionMigrationService.RetiredSystemCircleIds.Contains),
            "no app may still authorize them");

        var store = (await scope.Resolve<CircleNetworkStorage>().GetAsync(sam.Identity))!.PeerKeyStore;
        foreach (var id in V19ToV20VersionMigrationService.RetiredSystemCircleIds)
        {
            Assert.That(store.CircleGrants.Keys, Does.Not.Contain(id));
            Assert.That(store.AppGrants.Values.SelectMany(a => a.Keys), Does.Not.Contain(id));
        }

        Assert.That(store.CircleGrants.Keys, Is.Not.Empty, "the connection's other circles are untouched");

        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));
        Assert.DoesNotThrowAsync(() => migration.DeleteSystemCirclesAsync(ctx, CancellationToken.None),
            "running it again finds nothing to do");
    }

    /// <summary>
    /// The whole ladder, through the job entry point the scheduler uses: an identity holding the system
    /// circles, wound back to v0, comes out on the release version with them gone and its connection intact.
    /// </summary>
    /// <remarks>
    /// The data is today's shape, not a real v0 identity's, so this proves the ladder runs end to end
    /// over current data -- every step idempotent and in an order that works -- not that each step
    /// converts the shape it was written for.  The per-version tests cover that.
    /// </remarks>
    [Test]
    public async Task TheFullLadder_FromV0_ReachesTheReleaseVersion()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (scope, ctx) = await MigrationContextAsync(frodo);
        await SeedSystemCirclesAsync(scope, ctx, sam);

        var config = scope.Resolve<TenantConfigService>();
        await config.ForceVersionNumberAsync(0);

        var (iv, encryptedToken) = AesCbc.Encrypt(frodo.Token.ToPortableBytes(),
            scope.Resolve<TenantContext>().TemporalEncryptionKey);
        await scope.Resolve<VersionUpgradeService>().UpgradeAsync(new VersionUpgradeJobData
        {
            Tenant = frodo.Identity,
            Iv = iv,
            EncryptedToken = encryptedToken
        }, CancellationToken.None);

        // UpgradeAsync records a failure rather than throwing it; the log names the phase.
        var failure = await config.GetVersionFailureInfoAsync();
        Assert.That(failure, Is.Null, $"the ladder failed at v{failure?.FailedDataVersionNumber}; see the log");
        Assert.That((await config.GetVersionInfoAsync()).DataVersionNumber, Is.EqualTo(Odin.Services.Version.DataVersionNumber));

        var definitions = scope.Resolve<CircleDefinitionService>();
        foreach (var id in V19ToV20VersionMigrationService.RetiredSystemCircleIds)
        {
            Assert.That(await definitions.GetCircleAsync(id), Is.Null, "the system circles should be gone");
        }

        var icr = (await scope.Resolve<CircleNetworkStorage>().GetAsync(sam.Identity))!;
        Assert.That(icr.IsConnected(), Is.True, "the connection survives the ladder");
        Assert.That(icr.PeerKeyStore.CircleGrants.Keys, Does.Contain(BuiltinCircles.ChatCircle.Id.Value),
            "and keeps its Chat circle");
        Assert.That(icr.PeerKeyStore.CircleGrants.Keys,
            Has.None.Matches<Guid>(V19ToV20VersionMigrationService.RetiredSystemCircleIds.Contains));
    }

    private static async Task SeedSystemCirclesAsync(ILifetimeScope scope, IOdinContext ctx,
        OwnerSession member)
    {
        var definitions = scope.Resolve<CircleDefinitionService>();
        foreach (var id in V19ToV20VersionMigrationService.RetiredSystemCircleIds)
        {
            await definitions.EnsureCircleExistsAsync(new CircleDefinition
            {
                Id = id,
                Name = $"system {id:N}",
                Description = "",
                DriveGrants =
                [
                    new DriveGrantRequest
                    {
                        PermissionedDrive = new PermissionedDrive
                        {
                            Drive = WellKnownAppDrives.ChatDrive,
                            Permission = DrivePermission.Write | DrivePermission.React
                        }
                    }
                ],
                Permissions = new PermissionSet()
            });
        }

        var apps = scope.Resolve<IAppRegistrationService>();
        var chat = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        await apps.UpdateAuthorizedCirclesAsync(new UpdateAuthorizedCirclesRequest
        {
            AppId = chat!.AppId,
            AuthorizedCircles = (chat.AuthorizedCircles ?? []).Union(V19ToV20VersionMigrationService.RetiredSystemCircleIds).ToList(),
            CircleMemberPermissionGrant = chat.CircleMemberPermissionSetGrantRequest
        }, ctx);

        var storage = scope.Resolve<CircleNetworkStorage>();
        var icr = (await storage.GetAsync(member.Identity))!;
        foreach (var id in V19ToV20VersionMigrationService.RetiredSystemCircleIds)
        {
            icr.PeerKeyStore.CircleGrants[id] = new CircleGrant
            {
                CircleId = id,
                PermissionSet = new PermissionSet(),
                KeyStoreKeyEncryptedDriveGrants = []
            };

            if (!icr.PeerKeyStore.AppGrants.TryGetValue(SystemAppConstants.ChatAppId, out var chatGrants))
            {
                chatGrants = new Dictionary<Guid, AppCircleGrant>();
                icr.PeerKeyStore.AppGrants[SystemAppConstants.ChatAppId] = chatGrants;
            }

            chatGrants[id] = new AppCircleGrant
            {
                AppId = SystemAppConstants.ChatAppId,
                CircleId = id,
                PermissionSet = new PermissionSet(),
                KeyStoreKeyEncryptedDriveGrants = []
            };
        }

        await storage.UpsertAsync(icr, ctx);

        var db = scope.Resolve<IdentityDatabase>();
        foreach (var id in V19ToV20VersionMigrationService.RetiredSystemCircleIds)
        {
            Assert.That(await db.CircleMemberCached.GetCircleMembersAsync(id), Is.Not.Empty,
                "precondition: the member holds the system circle");
        }
    }
}
