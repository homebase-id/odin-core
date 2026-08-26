using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade.Version13tov14;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Profile;

/// <summary>
/// Covers the two halves of giving the chat app the relationship circles -- Friends, Family, Work,
/// Acquaintances (<see cref="SystemAppConstants.ChatRelationshipCircles"/>):
/// <list type="bullet">
/// <item>a new identity gets them chat-owned when the chat app is provisioned
/// (<c>TenantConfigService.RegisterChatAppAsync</c>), which the fixture's baseline already ran;</item>
/// <item>an identity that predates that -- where the owner console's setup wizard created them as owner
/// circles -- has them rebound by the v13 -> v14 migration.</item>
/// </list>
/// The wizard lives in odin-js and cannot be run from here, so the migration tests reproduce its end
/// state by handing the provisioned circles back to the owner
/// (<c>SetOwningAppAsync(id, null)</c>) and migrating from there. That the ids match the wizard's is
/// pinned separately by <see cref="RelationshipCircleIds_AreMd5OfTheirNames"/>.
/// </summary>
[TestFixture]
public class V13ToV14RelationshipCircleOwnershipTests : V2Fixture
{
    [Test]
    public async Task NewIdentity_GetsTheRelationshipCircles_OwnedByTheChatApp()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var circles = scope.Resolve<CircleDefinitionService>();

        foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
        {
            var circle = await circles.GetCircleAsync(declared.Id);

            Assert.That(circle, Is.Not.Null, $"'{declared.Name}' should be provisioned with the chat app");
            Assert.That(circle!.AppId, Is.EqualTo(SystemAppConstants.ChatAppId), $"'{declared.Name}' owner");
            Assert.That(circle.Name, Is.EqualTo(declared.Name));
            Assert.That(circle.Description, Is.EqualTo(declared.Description));
            Assert.That(circle.GrantOn, Is.EqualTo(CircleGrantOn.None),
                "these are manual-membership circles, not ambient ones");
            Assert.That(circle.Permissions.HasKey(PermissionKeys.ReadConnections), Is.True);
        }
    }

    /// <summary>
    /// The wizard still posts its own copies at the same ids. They must not take ownership back --
    /// initial setup registers the built-in apps before it walks the request's circles, and both
    /// create-if-missing.
    /// </summary>
    [Test]
    public async Task WizardCopiesInTheSetupRequest_DoNotTakeOwnershipBack()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        await owner.Admin.InitializeIdentity(new InitialSetupRequest
        {
            Circles = SystemAppConstants.ChatRelationshipCircles
                .Select(c => new CreateCircleRequest
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    Permissions = new PermissionSet(PermissionKeys.ReadConnections)
                    // AppId left null: an owner circle, exactly as the wizard posts it
                })
                .ToList()
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var circles = scope.Resolve<CircleDefinitionService>();

        foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
        {
            var circle = await circles.GetCircleAsync(declared.Id);
            Assert.That(circle!.AppId, Is.EqualTo(SystemAppConstants.ChatAppId),
                $"'{declared.Name}' should still be chat-owned after the wizard posts its copy");
        }
    }

    [Test]
    public async Task V17_BindsAllFourRelationshipCircles_ToTheChatApp()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var circles = scope.Resolve<CircleDefinitionService>();

        await MakeThemOwnerCirclesAsync(circles);

        var migration = scope.Resolve<V13ToV14VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
        {
            var after = await circles.GetCircleAsync(declared.Id);
            Assert.That(after!.AppId, Is.EqualTo(SystemAppConstants.ChatAppId),
                $"'{declared.Name}' should now be owned by the chat app");
        }
    }

    [Test]
    public async Task V17_ValidationFails_IfACircleWasLeftAnOwnerCircle()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var circles = scope.Resolve<CircleDefinitionService>();

        await MakeThemOwnerCirclesAsync(circles);

        // Validation without the upgrade: proves the assertion is real and not vacuously true.
        var migration = scope.Resolve<V13ToV14VersionMigrationService>();
        Assert.ThrowsAsync<Odin.Core.Exceptions.OdinSystemException>(
            async () => await migration.ValidateUpgradeAsync(ctx, CancellationToken.None));
    }

    [Test]
    public async Task V17_PreservesEverythingButOwnership()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var circles = scope.Resolve<CircleDefinitionService>();

        var friendsId = SystemAppConstants.ChatRelationshipCircles.First(c => c.Name == "Friends").Id;

        // A Friends circle the owner has since made their own: renamed, re-emojied, and carrying a
        // drive grant. None of that is the migration's to touch.
        await circles.DeleteAsync(friendsId);
        await circles.CreateAsync(new CreateCircleRequest
        {
            Id = friendsId,
            Name = "My Closest People",
            Description = "renamed by the owner",
            Emoji = "🫂",
            DriveGrants = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ChatDrive,
                        Permission = DrivePermission.Write
                    }
                }
            },
            Permissions = new PermissionSet()
        });

        var before = await circles.GetCircleAsync(friendsId);
        Assert.That(before!.AppId, Is.Null, "precondition: an owner circle");

        var migration = scope.Resolve<V13ToV14VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);

        var after = await circles.GetCircleAsync(friendsId);

        Assert.That(after!.AppId, Is.EqualTo(SystemAppConstants.ChatAppId), "ownership should have moved");
        Assert.That(after.Name, Is.EqualTo("My Closest People"), "the owner's rename must survive");
        Assert.That(after.Description, Is.EqualTo("renamed by the owner"));
        Assert.That(after.Emoji, Is.EqualTo("🫂"));
        Assert.That(after.GrantOn, Is.EqualTo(CircleGrantOn.None), "enrollment must not change");
        Assert.That(after.Designation, Is.EqualTo(before.Designation));
        Assert.That(after.DriveGrants.Single().PermissionedDrive.Drive, Is.EqualTo(SystemDriveConstants.ChatDrive),
            "the existing drive grant must be preserved");
        Assert.That(after.Created, Is.EqualTo(before.Created), "creation time must not change");
    }

    [Test]
    public async Task V17_LeavesCirclesOwnedByAnotherApp_Alone()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var circles = scope.Resolve<CircleDefinitionService>();

        await MakeThemOwnerCirclesAsync(circles);

        var workId = SystemAppConstants.ChatRelationshipCircles.First(c => c.Name == "Work").Id;
        await circles.SetOwningAppAsync(workId, SystemAppConstants.MailAppId);

        var migration = scope.Resolve<V13ToV14VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var work = await circles.GetCircleAsync(workId);
        Assert.That(work!.AppId, Is.EqualTo(SystemAppConstants.MailAppId),
            "a circle another app owns must not be taken over");

        var familyId = SystemAppConstants.ChatRelationshipCircles.First(c => c.Name == "Family").Id;
        var family = await circles.GetCircleAsync(familyId);
        Assert.That(family!.AppId, Is.EqualTo(SystemAppConstants.ChatAppId),
            "the rest of the set should still have moved");
    }

    [Test]
    public async Task V17_CreatesNothing_WhenTheIdentityHasNoSuchCircles()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var circles = scope.Resolve<CircleDefinitionService>();

        foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
        {
            await circles.DeleteAsync(declared.Id);
        }

        var migration = scope.Resolve<V13ToV14VersionMigrationService>();
        Assert.DoesNotThrowAsync(async () =>
        {
            await migration.UpgradeAsync(ctx, CancellationToken.None);
            await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);
        }, "an identity without these circles has nothing to migrate");

        foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
        {
            Assert.That(await circles.GetCircleAsync(declared.Id), Is.Null,
                $"the migration must not create a '{declared.Name}' circle that was never there");
        }
    }

    [Test]
    public async Task V17_IsIdempotent()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var circles = scope.Resolve<CircleDefinitionService>();

        await MakeThemOwnerCirclesAsync(circles);

        var migration = scope.Resolve<V13ToV14VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
        {
            var after = await circles.GetCircleAsync(declared.Id);
            Assert.That(after!.AppId, Is.EqualTo(SystemAppConstants.ChatAppId), $"'{declared.Name}' after a second run");
        }
    }

    /// <summary>
    /// The ids are not arbitrary constants: they are the ones the setup wizard assigns, which is
    /// <c>md5(name)</c> (odin-js <c>toGuidId</c>). Sharing them is the whole reason the migration finds
    /// the wizard's circles instead of creating a second set beside them.
    /// </summary>
    [Test]
    public void RelationshipCircleIds_AreMd5OfTheirNames()
    {
        foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
        {
            var md5 = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(declared.Name))).ToLowerInvariant();
            Assert.That(declared.Id, Is.EqualTo(Guid.Parse(md5)), $"'{declared.Name}' id should be md5('{declared.Name}')");
        }
    }

    /// <summary>
    /// Reproduces the pre-v17 state: the same circles, at the same ids, owned by nobody but the owner --
    /// which is how the setup wizard left them on every identity that ran it.
    /// </summary>
    private static async Task MakeThemOwnerCirclesAsync(CircleDefinitionService circles)
    {
        foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
        {
            await circles.SetOwningAppAsync(declared.Id, null);
        }
    }

    /// <summary>
    /// Builds an owner context carrying the master key by replaying the production path used by
    /// <c>VersionUpgradeService</c> (<see cref="OwnerAuthenticationService.UpdateOdinContextAsync"/>).
    /// </summary>
    private async Task<IOdinContext> BuildOwnerContextAsync(ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<OwnerAuthenticationService>();
        var odinContext = new OdinContext
        {
            Tenant = default,
            AuthTokenCreated = null,
            Caller = null
        };
        var clientContext = new OdinClientContext
        {
            CorsHostName = null,
            AccessRegistrationId = null,
            DevicePushNotificationKey = null,
            ClientIdOrDomain = null
        };

        await authService.UpdateOdinContextAsync(owner.Token, clientContext, odinContext);
        odinContext.Caller.AssertHasMasterKey();
        return odinContext;
    }
}
