using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.PublicPrivateKey;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.CircleMembership;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Port of <c>OwnerApi/Configuration/SystemInit/SystemInitializeConfigTests</c>. Initial setup is
/// itself the system under test: <c>isconfigured</c> flips false → true, the built-in drives and
/// system circles appear with the grants the product promises, and extra drives / circles named in
/// the request are created alongside them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The un-initialized tenant.</b> The original got one from
/// <c>RunBeforeAnyTests(initializeIdentity: false)</c>. Here
/// <see cref="V2Fixture.WarmTenantBaselineAsync"/> is overridden to log in — which sets the owner
/// password, required before <c>OdinHost.TakeBaselineAsync</c> snapshots the identity DB — and stop
/// there, deliberately skipping <c>Admin.InitializeIdentity()</c>. Same approach as
/// <c>Ported/DriveManagement/HandleDriveAddedRegressionTests</c>. Verified: the snapshot is taken
/// pre-init, so every test starts with <c>isconfigured</c> false and the first assertion of
/// <see cref="CanInitializeSystem_WithAllKeys"/> would fail loudly if that ever stopped holding.
/// </para>
/// <para>
/// The three originals each pinned a different identity (Pippin / Samwise / Frodo) only because a
/// <c>WebScaffold</c> run shares identities process-wide and initial setup is one-way. Per-test reset
/// removes that constraint, so all three run as the fixture default; a second identity would cost a
/// tenant materialisation plus a reset per test for nothing.
/// </para>
/// <para>
/// The whole fixture's SUT is the admin surface, so every call goes through
/// <see cref="OwnerSession.RefitFor{T}"/> rather than <c>owner.Admin</c> — splitting it would swap in
/// <c>OwnerAdmin</c>'s opinionated defaults (page size, metadata) under assertions that read them.
/// The public-key endpoints are anonymous guest routes, so they are reached with the host's raw client
/// rather than the owner factory, which would wrap them in shared-secret encryption.
/// </para>
/// <para>No caller matrix in the original and none added.</para>
/// </remarks>
[TestFixture]
public class SystemInitializeConfigTests : V2Fixture
{
    /// <summary>
    /// Logs the owner in (to set the password the snapshot baseline needs) and stops there. Skipping
    /// <c>InitializeIdentity</c> is the point: initial setup is what these tests exercise.
    /// </summary>
    protected override async Task WarmTenantBaselineAsync()
    {
        foreach (var identity in HostIdentities)
        {
            await LoginAsOwner(identity);
        }
    }

    [Test]
    public async Task CanInitializeSystem_WithAllKeys()
    {
        var owner = await LoginAsOwner();
        var config = owner.RefitFor<IRefitOwnerConfiguration>();

        //success = system drives created, other drives created
        var getIsIdentityConfiguredResponse1 = await config.IsIdentityConfigured();
        Assert.That(getIsIdentityConfiguredResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getIsIdentityConfiguredResponse1.Content, Is.False);

        var setupConfig = new InitialSetupRequest
        {
            Drives = null,
            Circles = null
        };

        var initIdentityResponse = await config.InitializeIdentity(setupConfig);
        Assert.That(initIdentityResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getIsIdentityConfiguredResponse = await config.IsIdentityConfigured();
        Assert.That(getIsIdentityConfiguredResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getIsIdentityConfiguredResponse.Content, Is.True);

        var keys = PublicKeyClient();

        //
        // Signing Key should exist
        //
        var signingKey = await keys.GetSigningPublicKey();
        Assert.That(signingKey.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(signingKey.Content!.PublicKey.Length, Is.GreaterThan(0));
        Assert.That(signingKey.Content.Crc32, Is.GreaterThan(0));

        //
        // Online Ecc key should exist
        var onlineEccPk = await keys.GetEccOnlinePublicKey();
        Assert.That(onlineEccPk.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(onlineEccPk.Content!.PublicKeyJwkBase64Url.Length, Is.GreaterThan(0));
        Assert.That(onlineEccPk.Content.CRC32c, Is.GreaterThan(0));

        //
        // Online Ecc key should exist
        var offlineEccPk = await keys.GetEccOfflinePublicKey();
        Assert.That(offlineEccPk.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(offlineEccPk.Content!.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task CanInitializeSystem_WithNoAdditionalDrives_and_NoAdditionalCircles()
    {
        var owner = await LoginAsOwner();
        var config = owner.RefitFor<IRefitOwnerConfiguration>();

        //success = system drives created, other drives created

        var getIsIdentityConfiguredResponse1 = await config.IsIdentityConfigured();
        Assert.That(getIsIdentityConfiguredResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getIsIdentityConfiguredResponse1.Content, Is.False);

        var setupConfig = new InitialSetupRequest
        {
            Drives = null,
            Circles = null
        };

        var initIdentityResponse = await config.InitializeIdentity(setupConfig);
        Assert.That(initIdentityResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getIsIdentityConfiguredResponse = await config.IsIdentityConfigured();
        Assert.That(getIsIdentityConfiguredResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getIsIdentityConfiguredResponse.Content, Is.True);

        //
        // system drives should be created
        //
        var createdDrivesResponse = await owner.RefitFor<IRefitDriveManagement>()
            .GetDrives(new GetDrivesRequest { PageNumber = 1, PageSize = 100 });
        Assert.That(createdDrivesResponse.Content, Is.Not.Null);

        var createdDrives = createdDrivesResponse.Content!;
        Assert.That(createdDrives.Results.Count, Is.EqualTo(BuiltinDrives.Protected.Count));

        void AssertDriveExists(TargetDrive drive) =>
            Assert.That(createdDrives.Results.Any(cd => cd.TargetDriveInfo == drive), Is.True,
                $"expected drive [{drive}] not found");

        AssertDriveExists(WellKnownAppDrives.ContactDrive);
        AssertDriveExists(WellKnownAppDrives.ProfileDrive);
        // WalletDrive is deliberately absent: it is no longer seeded for new identities, and only
        // the ones that already had it keep it.
        AssertDriveExists(WellKnownAppDrives.ChatDrive);
        AssertDriveExists(WellKnownAppDrives.MomentsDrive);
        AssertDriveExists(WellKnownAppDrives.MailDrive);
        AssertDriveExists(WellKnownAppDrives.FeedDrive);
        AssertDriveExists(WellKnownAppDrives.HomePageConfigDrive);
        AssertDriveExists(WellKnownAppDrives.PublicPostsChannelDrive);

        var getCircleDefinitionsResponse = await owner.RefitFor<IRefitOwnerCircleDefinition>()
            .GetCircleDefinitions(includeSystemCircle: true);
        Assert.That(getCircleDefinitionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getCircleDefinitionsResponse.Content, Is.Not.Null);
        var circleDefs = getCircleDefinitionsResponse.Content!.ToList();

        var seededCircles = BuiltinApps.SeededCircles.Select(c => (Guid)c.Id).Distinct().Count();
        Assert.That(circleDefs.Count, Is.EqualTo(SystemCircleConstants.AllSystemCircles.Count + seededCircles));

        var connectedIdentitiesSystemCircle = circleDefs.Single(c => c.Id == SystemCircleConstants.ConfirmedConnectionsCircleId);
        Assert.That((Guid)connectedIdentitiesSystemCircle.Id, Is.EqualTo((Guid)GuidId.FromString("we_are_connected")));
        // 10, not 9: WebDropDrive is anonymous-read, and HandleDriveAdded grants the system
        // circles read on every anonymous-read drive as it is created.  Seeding it followed
        // Webdrop joining BuiltinApps.Builtin.
        Assert.That(connectedIdentitiesSystemCircle.DriveGrants.Count(), Is.EqualTo(10));

        Assert.That(connectedIdentitiesSystemCircle.DriveGrants.SingleOrDefault(dg =>
            dg.PermissionedDrive.Drive == WellKnownAppDrives.ProfileDrive &&
            dg.PermissionedDrive.Permission == DrivePermission.Read), Is.Not.Null);

        Assert.That(connectedIdentitiesSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.ChatDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)), Is.Not.Null);

        Assert.That(connectedIdentitiesSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.MomentsDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)), Is.Not.Null);

        Assert.That(connectedIdentitiesSystemCircle.Permissions.Keys.Count, Is.EqualTo(1),
            "By default, the system circle should have 1 permission");
        Assert.That(connectedIdentitiesSystemCircle.Permissions.Keys.SingleOrDefault(k => k == PermissionKeys.AllowIntroductions),
            Is.Not.Null);

        Assert.That(connectedIdentitiesSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.MailDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)), Is.Not.Null);

        Assert.That(connectedIdentitiesSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.FeedDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)), Is.Not.Null);

        Assert.That(connectedIdentitiesSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.ListsDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)), Is.Not.Null);

        //

        var autoConnectionsSystemCircle = circleDefs.Single(c => c.Id == SystemCircleConstants.AutoConnectionsCircleId);
        Assert.That(autoConnectionsSystemCircle.Name, Is.EqualTo("Auto-connected Identities"));

        Assert.That(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.ChatDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)), Is.Not.Null);

        Assert.That(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
                dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.MomentsDrive &&
                      dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)), Is.Not.Null,
            "auto connections get write + react access to moments");

        Assert.That(autoConnectionsSystemCircle.Permissions.Keys.Exists(k => k == PermissionKeys.AllowIntroductions), Is.False);

        Assert.That(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.MailDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)), Is.Not.Null);

        Assert.That(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.FeedDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)), Is.Not.Null);

        // Granted via allowAnonymous read
        Assert.That(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.ProfileDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Read)), Is.Not.Null);

        // Granted via allowAnonymous read
        Assert.That(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.HomePageConfigDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Read)), Is.Not.Null);

        // Granted via allowAnonymous read
        Assert.That(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
            dg => dg.PermissionedDrive.Drive == WellKnownAppDrives.PublicPostsChannelDrive &&
                  dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Read)), Is.Not.Null);

        //
        // The Chat app should be registered with ReadWrite access to its app-level drives
        //
        var chatAppRegResponse = await owner.RefitFor<IRefitOwnerAppRegistration>()
            .GetRegisteredApp(new GetAppRequest { AppId = SystemAppConstants.ChatAppId });
        Assert.That(chatAppRegResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var chatAppReg = chatAppRegResponse.Content;
        Assert.That(chatAppReg, Is.Not.Null, "chat app was not found");

        void AssertChatAppReadWrite(TargetDrive drive)
        {
            Assert.That(chatAppReg!.Grant.DriveGrants.SingleOrDefault(dg =>
                    dg.PermissionedDrive.Drive == drive &&
                    dg.PermissionedDrive.Permission.HasFlag(DrivePermission.ReadWrite)), Is.Not.Null,
                $"chat app should have ReadWrite on drive [{drive}]");
        }

        AssertChatAppReadWrite(WellKnownAppDrives.ChatDrive);
        AssertChatAppReadWrite(WellKnownAppDrives.ListsDrive);
        AssertChatAppReadWrite(WellKnownAppDrives.StickerDrive);
        AssertChatAppReadWrite(WellKnownAppDrives.LocationDrive);
    }

    [Test]
    public async Task CanCreateSystemDrives_With_AdditionalDrivesAndCircles()
    {
        var owner = await LoginAsOwner();

        var standardProfileDrive = WellKnownAppDrives.ProfileDrive;

        var newDrive = new CreateDriveRequest
        {
            Name = "test",
            AllowAnonymousReads = true,
            Metadata = "",
            TargetDrive = TargetDrive.NewTargetDrive()
        };

        var additionalCircleRequest = new CreateCircleRequest
        {
            Id = Guid.NewGuid(),
            Name = "le circle",
            Description = "an additional circle",
            DriveGrants = new[]
            {
                new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = standardProfileDrive,
                        Permission = DrivePermission.Read
                    }
                }
            }
        };

        var setupConfig = new InitialSetupRequest
        {
            Drives = new List<CreateDriveRequest> { newDrive },
            Circles = new List<CreateCircleRequest> { additionalCircleRequest }
        };

        var initIdentityResponse = await owner.RefitFor<IRefitOwnerConfiguration>().InitializeIdentity(setupConfig);
        Assert.That(initIdentityResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        //check if system drives exist
        var expectedDrives = BuiltinDrives.Protected.Concat([newDrive.TargetDrive]).ToList();

        var createdDrivesResponse = await owner.RefitFor<IRefitDriveManagement>()
            .GetDrives(new GetDrivesRequest { PageNumber = 1, PageSize = 100 });
        Assert.That(createdDrivesResponse.Content, Is.Not.Null);
        var createdDrives = createdDrivesResponse.Content!;
        Assert.That(createdDrives.Results.Count, Is.EqualTo(expectedDrives.Count));

        foreach (var expectedDrive in expectedDrives)
        {
            Assert.That(createdDrives.Results.Any(cd => cd.TargetDriveInfo == expectedDrive), Is.True,
                $"expected drive [{expectedDrive}] not found");
        }

        var getCircleDefinitionsResponse = await owner.RefitFor<IRefitOwnerCircleDefinition>()
            .GetCircleDefinitions(includeSystemCircle: true);
        Assert.That(getCircleDefinitionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getCircleDefinitionsResponse.Content, Is.Not.Null);
        var circleDefs = getCircleDefinitionsResponse.Content!.ToList();

        //
        // System circle exists and has correct grants
        //

        var systemCircle = circleDefs.SingleOrDefault(c => c.Id == SystemCircleConstants.ConfirmedConnectionsCircleId);
        Assert.That(systemCircle, Is.Not.Null, "system circle should exist");
        Assert.That((Guid)systemCircle!.Id, Is.EqualTo((Guid)GuidId.FromString("we_are_connected")));
        Assert.That(systemCircle.Name, Is.EqualTo("Confirmed Connected Identities"));
        Assert.That(systemCircle.Description, Is.EqualTo(
            "Contains identities which you have confirmed as a connection, either by approving the connection yourself or upgrading an introduced connection"));
        Assert.That(systemCircle.Permissions.Keys.Count, Is.EqualTo(1),
            "By default, the system circle should have 1 permission");
        Assert.That(systemCircle.Permissions.Keys.SingleOrDefault(k => k == PermissionKeys.AllowIntroductions), Is.Not.Null);

        var newDriveGrant = systemCircle.DriveGrants.SingleOrDefault(dg =>
            dg.PermissionedDrive.Drive == newDrive.TargetDrive && dg.PermissionedDrive.Permission == DrivePermission.Read);
        Assert.That(newDriveGrant, Is.Not.Null, "The new drive should be in the system circle");

        var standardProfileDriveGrant =
            systemCircle.DriveGrants.SingleOrDefault(dg =>
                dg.PermissionedDrive.Drive == standardProfileDrive && dg.PermissionedDrive.Permission == DrivePermission.Read);
        Assert.That(standardProfileDriveGrant, Is.Not.Null, "The standard profile drive should be in the system circle");

        //note: the permission for chat drive is write
        var chatDriveGrant =
            systemCircle.DriveGrants.SingleOrDefault(dg =>
                dg.PermissionedDrive.Drive == WellKnownAppDrives.ChatDrive &&
                dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React));
        Assert.That(chatDriveGrant, Is.Not.Null, "the chat drive grant should exist in system circle");

        var momentsDriveGrant =
            systemCircle.DriveGrants.SingleOrDefault(dg =>
                dg.PermissionedDrive.Drive == WellKnownAppDrives.MomentsDrive &&
                dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React));
        Assert.That(momentsDriveGrant, Is.Not.Null, "the chat drive grant should exist in system circle");

        //
        // additional circle exists
        //
        var additionalCircle = circleDefs.SingleOrDefault(c => c.Id == additionalCircleRequest.Id);
        Assert.That(additionalCircle, Is.Not.Null);
        Assert.That(additionalCircle!.Name, Is.EqualTo("le circle"));
        Assert.That(additionalCircle.Description, Is.EqualTo("an additional circle"));
        Assert.That(
            additionalCircle.DriveGrants.Count(dg => dg.PermissionedDrive == additionalCircle.DriveGrants.Single().PermissionedDrive),
            Is.EqualTo(1),
            "The contact drive should be in the additional circle");
    }

    /// <summary>
    /// The public-key routes live under the anonymous guest surface, so they are called with a raw
    /// client bound to the identity's host header — not the owner factory, whose shared-secret
    /// handler would encrypt a request the endpoint expects in the clear.
    /// </summary>
    private IPublicPrivateKeyHttpClientForOwner PublicKeyClient()
    {
        var client = new System.Net.Http.HttpClient(Host.Server.CreateHandler())
        {
            BaseAddress = new Uri($"https://{PrimaryIdentity}/")
        };
        return RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
    }
}
