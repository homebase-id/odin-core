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
/// <c>RunBeforeAnyTests(initializeIdentity: false)</c>. Here <see cref="V2Fixture.InitializeIdentities"/>
/// is false, so the baseline still logs in — which sets the owner password, required before
/// <c>OdinHost.TakeBaselineAsync</c> snapshots the identity DB — and stops there, deliberately
/// skipping <c>Admin.InitializeIdentity()</c>. Same approach as
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
/// The public-key endpoints are anonymous guest routes, so they are reached with
/// <see cref="AnonymousHttp.AnonymousRefitFor{T}"/> rather than the owner factory, which would wrap
/// them in shared-secret encryption.
/// </para>
/// <para>No caller matrix in the original and none added.</para>
/// </remarks>
[TestFixture]
public class SystemInitializeConfigTests : V2Fixture
{
    /// <summary>The system under test is initial setup itself, so the tenants must not be initialized.</summary>
    protected override bool InitializeIdentities => false;

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

        // WalletDrive is deliberately absent: it is no longer seeded for new identities, and only
        // the ones that already had it keep it.
        Assert.That(createdDrives.Results.Select(cd => cd.TargetDriveInfo), Is.SupersetOf(new[]
        {
            WellKnownAppDrives.ContactDrive,
            WellKnownAppDrives.ProfileDrive,
            WellKnownAppDrives.ChatDrive,
            WellKnownAppDrives.MomentsDrive,
            WellKnownAppDrives.MailDrive,
            WellKnownAppDrives.FeedDrive,
            WellKnownAppDrives.HomePageConfigDrive,
            WellKnownAppDrives.PublicPostsChannelDrive
        }));

        var getCircleDefinitionsResponse = await owner.RefitFor<IRefitOwnerCircleDefinition>()
            .GetCircleDefinitions();
        Assert.That(getCircleDefinitionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getCircleDefinitionsResponse.Content, Is.Not.Null);
        var circleDefs = getCircleDefinitionsResponse.Content!.ToList();

        // Only the built-in apps' circles: the Confirmed and Auto system circles retired (#1809).
        var seededCircles = BuiltinApps.SeededCircles.Select(c => (Guid)c.Id).Distinct().Count();
        Assert.That(circleDefs.Count, Is.EqualTo(seededCircles));
        Assert.That(circleDefs.Any(c => (Guid)c.Id == (Guid)GuidId.FromString("we_are_connected")), Is.False,
            "the Confirmed Connections circle is no longer provisioned");

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

        Assert.That(createdDrives.Results.Select(cd => cd.TargetDriveInfo), Is.SupersetOf(expectedDrives));

        var getCircleDefinitionsResponse = await owner.RefitFor<IRefitOwnerCircleDefinition>()
            .GetCircleDefinitions();
        Assert.That(getCircleDefinitionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getCircleDefinitionsResponse.Content, Is.Not.Null);
        var circleDefs = getCircleDefinitionsResponse.Content!.ToList();

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
    /// The public-key routes live under the anonymous guest surface, so they are called with a
    /// credential-free client bound to the identity's host header — not the owner factory, whose
    /// shared-secret handler would encrypt a request the endpoint expects in the clear.
    /// </summary>
    private IPublicPrivateKeyHttpClientForOwner PublicKeyClient()
        => Host.AnonymousRefitFor<IPublicPrivateKeyHttpClientForOwner>(PrimaryIdentity);
}
