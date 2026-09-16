using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Apps;

/// <summary>
/// Port of <c>OwnerApi/Apps/AppRegistrationTests</c>. What the owner console can say about an app:
/// register it (with or without a CORS host, a drive grant, authorized circles), hand a client a
/// token, revoke it, and change its permissions after the fact.
/// </summary>
/// <remarks>
/// The transit permissions are the interesting half: asking for <c>UseTransitRead</c> or
/// <c>UseTransitWrite</c> is what silently earns an app its ICR key and read/write on the transient
/// temp drive, and giving the permission back has to take both away again -- so each of those four
/// combinations is pinned on registration and again on update.
/// <para>
/// The app-management endpoints are still V1 only, so the calls under test go through the V1 Refit
/// surface (<see cref="IRefitOwnerAppRegistration"/>) over the in-process pipeline, reached via
/// <see cref="AppsApi"/>. The register/read pair is used raw rather than through
/// <c>owner.Admin.RegisterApp</c> because several of these tests are about a registration that must
/// be refused, and the <c>Admin</c> helpers throw on non-2xx by design.
/// </para>
/// </remarks>
[TestFixture]
public class AppRegistrationTests : V2Fixture
{
    [Test]
    public async Task RegisterNewApp()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        await AddSampleAppNoDrive(owner, Guid.NewGuid(), "API Tests Sample App-register", "photos.odin.earth");
    }

    [Test]
    public async Task RegisterNewAppWith_UseTransitWrite_HasReadWriteOnTransientTempDrive_and_ICR_Key()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var svc = AppsApi(owner);

        var response = await svc.RegisterApp(new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = "App with Use Transit Read Access",
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.UseTransitWrite }),
            Drives = null,
            CorsHostName = default
        });
        Assert.That(response.IsSuccessStatusCode, Is.True);

        var appResponse = await svc.GetRegisteredApp(new GetAppRequest { AppId = applicationId });
        Assert.That(appResponse.IsSuccessStatusCode, Is.True, $"Could not retrieve the app {applicationId}");

        var registeredApp = appResponse.Content;
        Assert.That(registeredApp, Is.Not.Null, "App should exist");

        Assert.That(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite), Is.True);
        Assert.That(registeredApp.Grant.HasIcrKey, Is.True, "missing icr key but UseTransit is true");

        var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg =>
            dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
        Assert.That(transientDriveGrant, Is.Not.Null);
        Assert.That(transientDriveGrant!.PermissionedDrive.Permission.HasFlag(DrivePermission.ReadWrite), Is.True);
    }

    [Test]
    public async Task RegisterNewAppWith_UseTransitRead_HasReadWriteOnTransientTempDrive_and_ICR_Key()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var svc = AppsApi(owner);

        var response = await svc.RegisterApp(new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = "App with Use Transit Read Access",
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.UseTransitRead }),
            Drives = null,
            CorsHostName = default
        });
        Assert.That(response.IsSuccessStatusCode, Is.True);

        var appResponse = await svc.GetRegisteredApp(new GetAppRequest { AppId = applicationId });
        Assert.That(appResponse.IsSuccessStatusCode, Is.True, $"Could not retrieve the app {applicationId}");

        var registeredApp = appResponse.Content;
        Assert.That(registeredApp, Is.Not.Null, "App should exist");

        Assert.That(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead), Is.True);
        Assert.That(registeredApp.Grant.HasIcrKey, Is.True, "missing icr key but UseTransit is true");

        var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg =>
            dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
        Assert.That(transientDriveGrant, Is.Not.Null);
        Assert.That(transientDriveGrant!.PermissionedDrive.Permission.HasFlag(DrivePermission.ReadWrite), Is.True);
    }

    [Test]
    public async Task RegisterNewApp_Without_UseTransitWrite_HasNoIcrKey_AndDoesNotHavePermissionOn_TransientTempDrive()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var svc = AppsApi(owner);

        var response = await svc.RegisterApp(new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = "App with Use Transit Access",
            PermissionSet = new PermissionSet(new List<int>()),
            Drives = null,
            CorsHostName = default
        });
        Assert.That(response.IsSuccessStatusCode, Is.True);

        var appResponse = await svc.GetRegisteredApp(new GetAppRequest { AppId = applicationId });
        Assert.That(appResponse.IsSuccessStatusCode, Is.True, $"Could not retrieve the app {applicationId}");

        var registeredApp = appResponse.Content;
        Assert.That(registeredApp, Is.Not.Null, "App should exist");

        Assert.That(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite), Is.False);
        Assert.That(registeredApp.Grant.HasIcrKey, Is.False,
            "Icr key should not be present when UseTransit permission is not given");

        var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg =>
            dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
        Assert.That(transientDriveGrant, Is.Null);
    }

    [Test]
    public async Task RegisterNewApp_Without_UseTransitRead_HasNoIcrKey_AndDoesNotHavePermissionOn_TransientTempDrive()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var svc = AppsApi(owner);

        var response = await svc.RegisterApp(new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = "App with Use Transit Access",
            PermissionSet = new PermissionSet(new List<int>()),
            Drives = null,
            CorsHostName = default
        });
        Assert.That(response.IsSuccessStatusCode, Is.True);

        var appResponse = await svc.GetRegisteredApp(new GetAppRequest { AppId = applicationId });
        Assert.That(appResponse.IsSuccessStatusCode, Is.True, $"Could not retrieve the app {applicationId}");

        var registeredApp = appResponse.Content;
        Assert.That(registeredApp, Is.Not.Null, "App should exist");

        Assert.That(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead), Is.False);
        Assert.That(registeredApp.Grant.HasIcrKey, Is.False,
            "Icr key should not be present when UseTransit permission is not given");

        var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg =>
            dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
        Assert.That(transientDriveGrant, Is.Null);
    }

    [Test]
    public async Task AppPermissionUpdate_Keeps_TransientDriveWhen_UseTransitWrite_IsGranted()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var appPermissionsGrant = new PermissionSetGrantRequest
        {
            Drives = null,
            PermissionSet = new PermissionSet([])
        };

        await owner.Admin.RegisterApp(applicationId, appPermissionsGrant);

        //
        // Should not have icr or transient temp drive
        //
        var appReg = await GetSampleApp(owner, applicationId);
        Assert.That(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite), Is.False);
        Assert.That(appReg.Grant.HasIcrKey, Is.False);
        Assert.That(
            appReg.Grant.DriveGrants.SingleOrDefault(dg =>
                dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive),
            Is.Null);

        appPermissionsGrant.PermissionSet = new PermissionSet(new List<int> { PermissionKeys.UseTransitWrite });
        await UpdateAppPermissions(owner, applicationId, appPermissionsGrant);

        var updatedAppReg = await GetSampleApp(owner, applicationId);
        Assert.That(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite), Is.True);
        Assert.That(updatedAppReg.Grant.HasIcrKey, Is.True);

        var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg =>
            dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
        Assert.That(transientDriveGrant, Is.Not.Null);
    }

    [Test]
    public async Task AppPermissionUpdate_Keeps_TransientDriveWhen_UseTransitRead_IsGranted()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var appPermissionsGrant = new PermissionSetGrantRequest
        {
            Drives = null,
            PermissionSet = new PermissionSet(new List<int>())
        };

        await owner.Admin.RegisterApp(applicationId, appPermissionsGrant);

        //
        // Should not have icr or transient temp drive
        //
        var appReg = await GetSampleApp(owner, applicationId);
        Assert.That(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead), Is.False);
        Assert.That(appReg.Grant.HasIcrKey, Is.False);
        Assert.That(
            appReg.Grant.DriveGrants.SingleOrDefault(dg =>
                dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive),
            Is.Null);

        appPermissionsGrant.PermissionSet = new PermissionSet(new List<int> { PermissionKeys.UseTransitRead });
        await UpdateAppPermissions(owner, applicationId, appPermissionsGrant);

        var updatedAppReg = await GetSampleApp(owner, applicationId);
        Assert.That(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead), Is.True);
        Assert.That(updatedAppReg.Grant.HasIcrKey, Is.True);

        var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg =>
            dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
        Assert.That(transientDriveGrant, Is.Not.Null);
    }

    [Test]
    public async Task RevokingUseTransitWrite_RemovesIcrKey_And_TransientTempDrive()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var appPermissionsGrant = new PermissionSetGrantRequest
        {
            Drives = null,
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.UseTransitWrite })
        };

        await owner.Admin.RegisterApp(applicationId, appPermissionsGrant);

        var appReg = await GetSampleApp(owner, applicationId);
        Assert.That(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite), Is.True);
        Assert.That(appReg.Grant.HasIcrKey, Is.True);

        appPermissionsGrant.PermissionSet = new PermissionSet(); //remove use transit
        await UpdateAppPermissions(owner, applicationId, appPermissionsGrant);

        var updatedAppReg = await GetSampleApp(owner, applicationId);
        Assert.That(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite), Is.False);
        Assert.That(updatedAppReg.Grant.HasIcrKey, Is.False);

        var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg =>
            dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
        Assert.That(transientDriveGrant, Is.Null);
    }

    [Test]
    public async Task RevokingUseTransitRead_RemovesIcrKey_And_TransientTempDrive()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var appPermissionsGrant = new PermissionSetGrantRequest
        {
            Drives = null,
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.UseTransitRead })
        };

        await owner.Admin.RegisterApp(applicationId, appPermissionsGrant);

        var appReg = await GetSampleApp(owner, applicationId);
        Assert.That(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead), Is.True);
        Assert.That(appReg.Grant.HasIcrKey, Is.True);

        appPermissionsGrant.PermissionSet = new PermissionSet(); //remove use transit
        await UpdateAppPermissions(owner, applicationId, appPermissionsGrant);

        var updatedAppReg = await GetSampleApp(owner, applicationId);
        Assert.That(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead), Is.False);
        Assert.That(updatedAppReg.Grant.HasIcrKey, Is.False);

        var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg =>
            dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
        Assert.That(transientDriveGrant, Is.Null);
    }

    [Test]
    public async Task FailToRegisterNewAppWithInvalidCorsHostName()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var svc = AppsApi(owner);

        var response = await svc.RegisterApp(new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = "API Tests Sample App-register",
            PermissionSet = null,
            Drives = null,
            CorsHostName = "*.odin.earth"
        });

        Assert.That(response.IsSuccessStatusCode, Is.False,
            $"Should have failed to add app registration.  Status code was {response.StatusCode}");

        var appResponse = await svc.GetRegisteredApp(new GetAppRequest { AppId = applicationId });
        Assert.That(appResponse.IsSuccessStatusCode, Is.True, $"Could not retrieve the app {applicationId}");
        Assert.That(appResponse.Content, Is.Null, "There should be no app");
    }

    [Test]
    public async Task RegisterNewAppWithCorsHostNameAndPort()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var corsHostName = "somewhere.odin.earth:444";
        var svc = AppsApi(owner);

        var response = await svc.RegisterApp(new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = "API Tests Sample App-register",
            PermissionSet = null,
            Drives = null,
            CorsHostName = corsHostName
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Status code was {response.StatusCode}");

        var appResponse = await svc.GetRegisteredApp(new GetAppRequest { AppId = applicationId });
        Assert.That(appResponse.IsSuccessStatusCode, Is.True, $"Could not retrieve the app {applicationId}");
        Assert.That(appResponse.Content, Is.Not.Null);
        Assert.That(appResponse.Content!.CorsHostName, Is.EqualTo(corsHostName));
    }

    [Test]
    public async Task FailToRegisterNewAppWithCorsHostNameAndInvalidPort()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var svc = AppsApi(owner);

        var response = await svc.RegisterApp(new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = "API Tests Sample App-register",
            PermissionSet = null,
            Drives = null,
            CorsHostName = "somewhere.odin.earth:3AC"
        });

        Assert.That(response.IsSuccessStatusCode, Is.False, $"Status code was {response.StatusCode}");

        var appResponse = await svc.GetRegisteredApp(new GetAppRequest { AppId = applicationId });
        Assert.That(appResponse.IsSuccessStatusCode, Is.True, $"Could not retrieve the app {applicationId}");
        Assert.That(appResponse.Content, Is.Null, "There should be no app");
    }

    [Test]
    public async Task RegisterNewAppWithDriveAndPermissions()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var name = "API TestApp";

        var targetDrive1 = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive1, "Drive 1 for Circle Test", allowAnonymousReads: false);

        var dgr1 = new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = targetDrive1,
                Permission = DrivePermission.ReadWrite
            }
        };

        var dgr2 = new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = targetDrive1,
                Permission = DrivePermission.Write
            }
        };

        var circle1Id = await CreateCircleWithDrive(owner, "Circle 1",
            [PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections],
            [new PermissionedDrive { Drive = targetDrive1, Permission = DrivePermission.Read }]);

        var svc = AppsApi(owner);
        var request = new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = name,
            Drives = new List<DriveGrantRequest> { dgr1, dgr2 },
            PermissionSet = new PermissionSet(new List<int>
                { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
            AuthorizedCircles = new List<Guid> { circle1Id },
            CircleMemberPermissionGrant = new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest> { dgr2 },
                PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadConnections })
            }
        };

        var response = await svc.RegisterApp(request);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var savedApp = await GetSampleApp(owner, applicationId);
        Assert.That(savedApp.AppId.Value, Is.EqualTo(applicationId));
        Assert.That(savedApp.Name, Is.EqualTo(request.Name));

        Assert.That(savedApp.AuthorizedCircles, Is.EquivalentTo(request.AuthorizedCircles));

        Assert.That(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
            Is.EquivalentTo(request.Drives.Select(p => p.PermissionedDrive)));
        Assert.That(savedApp.Grant.PermissionSet, Is.EqualTo(request.PermissionSet));

        Assert.That(savedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
            Is.EquivalentTo(request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList()));
        Assert.That(savedApp.CircleMemberPermissionSetGrantRequest.PermissionSet,
            Is.EqualTo(request.CircleMemberPermissionGrant.PermissionSet));
    }

    [Test]
    public async Task RevokeAppRegistration()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appId = Guid.NewGuid();

        await AddSampleAppNoDrive(owner, appId, "API Tests Sample App-revoke", "photos.odin.earth");

        var svc = AppsApi(owner);
        var revokeResponse = await svc.RevokeApp(new GetAppRequest { AppId = appId });

        Assert.That(revokeResponse.IsSuccessStatusCode, Is.True);
        Assert.That(revokeResponse.Content?.Success, Is.True);

        var savedApp = await GetSampleApp(owner, appId);
        Assert.That(savedApp.IsRevoked, Is.True);
    }

    [Test]
    public async Task RegisterAppOnClient()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appId = Guid.NewGuid();
        await AddSampleAppNoDrive(owner, appId, "API Tests Sample App-reg-app-device", "app.somewhere.org");

        var svc = AppsApi(owner);

        var clientPrivateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var clientKeyPair = new EccFullKeyData(clientPrivateKey, EccKeySize.P384, 1);

        var regResponse = await svc.RegisterAppOnClientUsingEcc(new AppClientRegistrationRequest
        {
            AppId = appId,
            JwkBase64UrlPublicKey = clientKeyPair.PublicKeyJwkBase64Url(),
            ClientFriendlyName = "Some phone"
        });
        Assert.That(regResponse.IsSuccessStatusCode, Is.True);

        var reply = regResponse.Content;
        Assert.That(reply, Is.Not.Null);

        var remotePublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(reply.ExchangePublicKeyJwkBase64Url);
        var remoteSalt = Convert.FromBase64String(reply.ExchangeSalt64);

        var exchangeSecret = clientKeyPair.GetEcdhSharedSecret(clientPrivateKey, remotePublicKey, remoteSalt);
        var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();
        Assert.That(reply.EncryptionVersion, Is.EqualTo(1));

        var youAuthResponse = await svc.ExchangeDigestForToken(new YouAuthTokenRequest
        {
            SecretDigest = exchangeSecretDigest
        });

        var token = youAuthResponse.Content;
        Assert.That(token, Is.Not.Null);

        Assert.That(token.Base64SharedSecretCipher, Is.Not.Null.And.Not.Empty);
        Assert.That(token.Base64SharedSecretIv, Is.Not.Null.And.Not.Empty);
        Assert.That(token.Base64ClientAuthTokenCipher, Is.Not.Null.And.Not.Empty);
        Assert.That(token.Base64ClientAuthTokenIv, Is.Not.Null.And.Not.Empty);

        var sharedSecretCipher = Convert.FromBase64String(token.Base64SharedSecretCipher!);
        var sharedSecretIv = Convert.FromBase64String(token.Base64SharedSecretIv!);
        var sharedSecret = AesCbc.Decrypt(sharedSecretCipher, exchangeSecret, sharedSecretIv);
        Assert.That(sharedSecret, Is.Not.Null.And.Not.Empty);

        var clientAuthTokenCipher = Convert.FromBase64String(token.Base64ClientAuthTokenCipher!);
        var clientAuthTokenIv = Convert.FromBase64String(token.Base64ClientAuthTokenIv!);
        var clientAuthTokenBytes = AesCbc.Decrypt(clientAuthTokenCipher, exchangeSecret, clientAuthTokenIv);
        Assert.That(clientAuthTokenBytes, Is.Not.Null.And.Not.Empty);

        var authToken = ClientAuthenticationToken.FromPortableBytes(clientAuthTokenBytes);
        var cat = new ClientAccessToken
        {
            Id = authToken.Id,
            AccessTokenHalfKey = authToken.AccessTokenHalfKey,
            ClientTokenType = authToken.ClientTokenType,
            SharedSecret = sharedSecret.ToSensitiveByteArray()
        };

        Assert.That(cat.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(cat.AccessTokenHalfKey, Is.Not.Null);
        Assert.That(cat.AccessTokenHalfKey.GetKey().Length, Is.EqualTo(16));
        Assert.That(cat.AccessTokenHalfKey.IsSet(), Is.True);
        Assert.That(cat.IsValid(), Is.True);

        Assert.That(cat.SharedSecret, Is.Not.Null);
        Assert.That(cat.SharedSecret.GetKey().Length, Is.EqualTo(16));
    }

    [Test]
    public async Task UpdateAppPermissions()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var name = "API TestApp";

        var targetDrive1 = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive1, "Drive 1 for Circle Test", allowAnonymousReads: false);

        var dgr1 = new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = targetDrive1,
                Permission = DrivePermission.ReadWrite
            }
        };

        var dgr2 = new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = targetDrive1,
                Permission = DrivePermission.Write
            }
        };

        var dgr3 = new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = targetDrive1,
                Permission = DrivePermission.ReadWrite
            }
        };

        var circle1Id = await CreateCircleWithDrive(owner, "Circle 1",
            [PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections],
            [new PermissionedDrive { Drive = targetDrive1, Permission = DrivePermission.Read }]);

        var svc = AppsApi(owner);
        var request = new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = name,
            Drives = new List<DriveGrantRequest> { dgr1, dgr2 },
            PermissionSet = new PermissionSet(new List<int>
                { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
            AuthorizedCircles = new List<Guid> { circle1Id },
            CircleMemberPermissionGrant = new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest> { dgr2 },
                PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadConnections })
            },
        };

        var response = await svc.RegisterApp(request);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var savedApp = await GetSampleApp(owner, applicationId);
        Assert.That(savedApp.AppId.Value, Is.EqualTo(applicationId));
        Assert.That(savedApp.Name, Is.EqualTo(request.Name));

        Assert.That(savedApp.AuthorizedCircles, Is.EquivalentTo(request.AuthorizedCircles));

        Assert.That(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
            Is.EquivalentTo(request.Drives.Select(p => p.PermissionedDrive)));
        Assert.That(savedApp.Grant.PermissionSet, Is.EqualTo(request.PermissionSet));

        Assert.That(savedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
            Is.EquivalentTo(request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList()));
        Assert.That(savedApp.CircleMemberPermissionSetGrantRequest.PermissionSet,
            Is.EqualTo(request.CircleMemberPermissionGrant.PermissionSet));

        var updateRequest = new UpdateAppPermissionsRequest
        {
            AppId = applicationId,
            PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadConnectionRequests }),
            Drives = new List<DriveGrantRequest> { dgr3 }
        };

        await svc.UpdateAppPermissions(updateRequest);

        var updatedApp = await GetSampleApp(owner, applicationId);
        // be sure the permissions are updated
        Assert.That(updatedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
            Is.EquivalentTo(updateRequest.Drives.Select(p => p.PermissionedDrive)));
        Assert.That(updatedApp.Grant.PermissionSet, Is.EqualTo(updateRequest.PermissionSet));

        // be sure the other fields did not change
        Assert.That(updatedApp.AuthorizedCircles, Is.EquivalentTo(request.AuthorizedCircles));
        Assert.That(updatedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
            Is.EquivalentTo(request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList()));
        Assert.That(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet,
            Is.EqualTo(request.CircleMemberPermissionGrant.PermissionSet));
    }

    [Test]
    public async Task UpdateAuthorizedCircles()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var applicationId = Guid.NewGuid();
        var name = "API TestApp";

        var targetDrive1 = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive1, "Drive 1 for Circle Test", allowAnonymousReads: false);

        var dgr1 = new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = targetDrive1,
                Permission = DrivePermission.ReadWrite
            }
        };

        var dgr2 = new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = targetDrive1,
                Permission = DrivePermission.Write
            }
        };

        var dgr3 = new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = targetDrive1,
                Permission = DrivePermission.ReadWrite
            }
        };

        var circle1Id = await CreateCircleWithDrive(owner, "Circle 1",
            [PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections],
            [new PermissionedDrive { Drive = targetDrive1, Permission = DrivePermission.Read }]);

        var circle2Id = await CreateCircleWithDrive(owner, "Circle 2",
            [PermissionKeys.ReadConnections],
            [new PermissionedDrive { Drive = targetDrive1, Permission = DrivePermission.Write }]);

        var svc = AppsApi(owner);
        var request = new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = name,
            Drives = new List<DriveGrantRequest> { dgr1, dgr2 },
            PermissionSet = new PermissionSet(new List<int>
                { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
            AuthorizedCircles = new List<Guid> { circle1Id },
            CircleMemberPermissionGrant = new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest> { dgr2 },
                PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadConnections })
            }
        };

        var response = await svc.RegisterApp(request);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var savedApp = await GetSampleApp(owner, applicationId);
        Assert.That(savedApp.AppId.Value, Is.EqualTo(applicationId));
        Assert.That(savedApp.Name, Is.EqualTo(request.Name));

        Assert.That(savedApp.AuthorizedCircles, Is.EquivalentTo(request.AuthorizedCircles));

        Assert.That(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
            Is.EquivalentTo(request.Drives.Select(p => p.PermissionedDrive)));
        Assert.That(savedApp.Grant.PermissionSet, Is.EqualTo(request.PermissionSet));

        Assert.That(savedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
            Is.EquivalentTo(request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList()));
        Assert.That(savedApp.CircleMemberPermissionSetGrantRequest.PermissionSet,
            Is.EqualTo(request.CircleMemberPermissionGrant.PermissionSet));

        var updateRequest = new UpdateAuthorizedCirclesRequest
        {
            AppId = applicationId,
            AuthorizedCircles = new List<Guid> { circle2Id },
            CircleMemberPermissionGrant = new PermissionSetGrantRequest
            {
                PermissionSet = new PermissionSet(new List<int> { PermissionKeys.ReadConnectionRequests }),
                Drives = new List<DriveGrantRequest> { dgr3 }
            }
        };

        await svc.UpdateAuthorizedCircles(updateRequest);

        var updatedApp = await GetSampleApp(owner, applicationId);
        // be sure the permissions are updated
        Assert.That(updatedApp.AuthorizedCircles, Is.EquivalentTo(updateRequest.AuthorizedCircles));
        Assert.That(updatedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
            Is.EquivalentTo(updateRequest.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList()));
        Assert.That(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet,
            Is.EqualTo(updateRequest.CircleMemberPermissionGrant.PermissionSet));

        // be sure the other fields did not change
        Assert.That(updatedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
            Is.EquivalentTo(request.Drives.Select(p => p.PermissionedDrive)));
        Assert.That(updatedApp.Grant.PermissionSet, Is.EqualTo(request.PermissionSet));
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The V1 app-management Refit surface as the logged-in owner. Unlike <c>owner.Admin</c>, this
    /// hands back the raw <c>ApiResponse</c>, which the refusal tests here need.
    /// </summary>
    private static IRefitOwnerAppRegistration AppsApi(OwnerSession owner)
    {
        var (client, ss) = owner.NewAdminHttpClient();
        return RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ss);
    }

    private static async Task<RedactedAppRegistration> AddSampleAppNoDrive(
        OwnerSession owner, Guid applicationId, string name, string corsHostName)
    {
        var svc = AppsApi(owner);
        var request = new AppRegistrationRequest
        {
            AppId = applicationId,
            Name = name,
            PermissionSet = null,
            Drives = null,
            CorsHostName = corsHostName
        };

        var response = await svc.RegisterApp(request);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        var appReg = response.Content;
        Assert.That(appReg, Is.Not.Null);

        var savedApp = await GetSampleApp(owner, applicationId);
        Assert.That(savedApp.AppId.Value, Is.EqualTo(applicationId));
        Assert.That(savedApp.Name, Is.EqualTo(request.Name));
        Assert.That(savedApp.CorsHostName, Is.EqualTo(request.CorsHostName));

        return appReg;
    }

    private static async Task<RedactedAppRegistration> GetSampleApp(OwnerSession owner, Guid appId)
    {
        var svc = AppsApi(owner);
        var appResponse = await svc.GetRegisteredApp(new GetAppRequest { AppId = appId });
        Assert.That(appResponse.IsSuccessStatusCode, Is.True, $"Could not retrieve the app {appId}");
        Assert.That(appResponse.Content, Is.Not.Null, $"Could not retrieve the app {appId}");
        return appResponse.Content;
    }

    private static async Task UpdateAppPermissions(OwnerSession owner, Guid appId, PermissionSetGrantRequest grant)
    {
        var svc = AppsApi(owner);
        await svc.UpdateAppPermissions(new UpdateAppPermissionsRequest
        {
            AppId = appId,
            Drives = grant.Drives,
            PermissionSet = grant.PermissionSet
        });
    }

    /// <summary>
    /// Creates a circle granting <paramref name="drives"/> and <paramref name="permissionKeys"/>, and
    /// returns its id -- the app registrations below only ever need the id to authorize.
    /// </summary>
    private static async Task<Guid> CreateCircleWithDrive(
        OwnerSession owner, string circleName, List<int> permissionKeys, List<PermissionedDrive> drives)
    {
        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, circleName, new PermissionSetGrantRequest
        {
            Drives = drives.Select(d => new DriveGrantRequest { PermissionedDrive = d }).ToList(),
            PermissionSet = new PermissionSet(permissionKeys)
        });
        return circleId;
    }
}
