using Refit;
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using System.Reflection;
using System.Threading.Tasks;
using Youverse.Core;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drives;
using Youverse.Hosting.Authentication.ClientToken;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Tests.Anonymous.ApiClient;
using Youverse.Hosting.Tests.AppAPI.ApiClient.Auth;
using Youverse.Hosting.Tests.AppAPI.Circle;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;
using Youverse.Hosting.Tests.OwnerApi.Authentication;
using Youverse.Hosting.Tests.OwnerApi.Circle;

namespace Youverse.Hosting.Tests.AppAPI.Authentication
{
    [TestFixture]
    public class AppAuthenticationTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }
        
        [Test]
        public async Task AppCanLogoutItself()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

            var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);
            var appId = Guid.NewGuid();

            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = appDrive.TargetDriveInfo,
                            Permission = DrivePermission.All
                        }
                    }
                },
                PermissionSet = new PermissionSet(PermissionKeys.All)
            };

            var appRegistration = await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);
            var appApiClient = _scaffold.CreateAppClient(TestIdentities.Samwise, appId);

            var clients = await ownerClient.Apps.GetRegisteredClients();
            Assert.IsNotNull(clients.SingleOrDefault(c => c.AppId == appId && c.AccessRegistrationId == appApiClient.AccessRegistrationId));

            await appApiClient.Logout();

            //log out the app
            var updatedClients = await ownerClient.Apps.GetRegisteredClients();
            Assert.IsTrue(!updatedClients.Any());
        }

        [Test]
        public async Task CanPreauthForWebsocket()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

            var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);
            var appId = Guid.NewGuid();

            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = appDrive.TargetDriveInfo,
                            Permission = DrivePermission.All
                        }
                    }
                },
                PermissionSet = new PermissionSet(PermissionKeys.All)
            };

            var appRegistration = await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);
            var appApiClient = _scaffold.CreateAppClient(TestIdentities.Samwise, appId);

            var response = await appApiClient.PreAuth();
            
            Assert.IsTrue(response.Headers.TryGetValues("Set-Cookie", out var values));
            Assert.IsTrue(values.Any(v=>v.StartsWith(ClientTokenConstants.ClientAuthTokenCookieName)));
        }
    }
}