using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Circle
{
    public class CircleDefinitionTests
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

        [SetUp]
        public void Setup()
        {
            //runs before each test 
            //_scaffold.DeleteData(); 
        }

        [Test]
        public async Task FailToCreateInvalidCircle()
        {
            var targetDrive1 = TargetDrive.NewTargetDrive();
            var targetDrive2 = TargetDrive.NewTargetDrive();

            var identity = TestIdentities.Samwise;
            await _scaffold.OwnerApi.CreateDrive(identity, targetDrive1, "Drive 1 for Circle Test", "", false);
            await _scaffold.OwnerApi.CreateDrive(identity, targetDrive2, "Drive 2 for Circle Test", "", false);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var requestWithNoPermissionsOrDrives = new CreateCircleRequest()
                {
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = new List<DriveGrantRequest>() { },
                    Permissions = new PermissionSet()
                };

                var createCircleResponse = await svc.CreateCircleDefinition(requestWithNoPermissionsOrDrives);
                Assert.IsTrue(createCircleResponse.StatusCode == HttpStatusCode.InternalServerError, $"Failed.  Actual response {createCircleResponse.StatusCode}");
            }
        }

        [Test]
        public async Task CanCreateCircle()
        {
            var targetDrive1 = TargetDrive.NewTargetDrive();
            var targetDrive2 = TargetDrive.NewTargetDrive();

            var identity = TestIdentities.Samwise;
            await _scaffold.OwnerApi.CreateDrive(identity, targetDrive1, "Drive 1 for Circle Test", "", false);
            await _scaffold.OwnerApi.CreateDrive(identity, targetDrive2, "Drive 2 for Circle Test", "", false);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var dgr1 = new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = targetDrive1,
                        Permission = DrivePermission.ReadWrite
                    }
                };

                var dgr2 = new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = targetDrive1,
                        Permission = DrivePermission.Write
                    }
                };

                var request = new CreateCircleRequest()
                {
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections })
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                var circle = definitionList.Single();

                // Assert.IsNotNull(circle.DrivesGrants.SingleOrDefault(d => d.Drive.Alias == dgr1.Drive.Alias && d.Drive.Type == dgr1.Drive.Type && d.Permission == dgr1.Permission));
                // Assert.IsNotNull(circle.DrivesGrants.SingleOrDefault(d => d.Drive.Alias == dgr2.Drive.Alias && d.Drive.Type == dgr2.Drive.Type && d.Permission == dgr2.Permission));

                Assert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d.PermissionedDrive == dgr1.PermissionedDrive));
                Assert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d.PermissionedDrive == dgr1.PermissionedDrive));

                Assert.IsTrue(circle.Permissions.HasKey(PermissionKeys.ReadCircleMembership));
                Assert.IsTrue(circle.Permissions.HasKey(PermissionKeys.ReadConnections));

                Assert.AreEqual(request.Name, circle.Name);
                Assert.AreEqual(request.Description, circle.Description);
                // CollectionAssert.AreEquivalent(request.Drives, circle.Drives);
                Assert.IsTrue(request.Permissions == circle.Permissions);

                // cleanup
                await svc.DeleteCircleDefinition(circle.Id);
            }
        }

        [Test]
        public async Task CanGetListOfCircleDefinitions()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var request1 = new CreateCircleRequest()
                {
                    Name = "Test Circle 1",
                    Description = "Test circle description 1",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership })
                };

                var createCircleResponse1 = await svc.CreateCircleDefinition(request1);
                Assert.IsTrue(createCircleResponse1.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse1.StatusCode}");


                var request2 = new CreateCircleRequest()
                {
                    Name = "Test Circle 2",
                    Description = "Test circle description 2",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections })
                };

                var createCircleResponse2 = await svc.CreateCircleDefinition(request2);
                Assert.IsTrue(createCircleResponse2.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse2.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                Assert.IsNotNull(getCircleDefinitionsResponse.Content);
                var definitionList = getCircleDefinitionsResponse.Content.ToList();

                Assert.IsTrue(definitionList.Count() == 2);

                var circle1 = definitionList[0];
                Assert.AreEqual(request1.Name, circle1.Name);
                Assert.AreEqual(request1.Description, circle1.Description);
                CollectionAssert.AreEqual(request1.DriveGrants, circle1.DriveGrants);
                Assert.IsTrue(request1.Permissions == circle1.Permissions);

                var circle2 = definitionList[1];
                Assert.AreEqual(request2.Name, circle2.Name);
                Assert.AreEqual(request2.Description, circle2.Description);
                CollectionAssert.AreEqual(request2.DriveGrants, circle2.DriveGrants);
                Assert.IsTrue(request2.Permissions == circle2.Permissions);

                // cleanup

                await svc.DeleteCircleDefinition(circle1.Id);
                await svc.DeleteCircleDefinition(circle2.Id);
            }
        }

        [Test]
        public async Task CanUpdateCircleDefinition_NoMembershipReconciliation()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership })
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                var circle = definitionList.Single();
                Assert.IsTrue(circle.Permissions.HasKey(PermissionKeys.ReadCircleMembership));


                //

                circle.Name = "updated name";
                circle.Description = "updated description";

                circle.DriveGrants = null;
                circle.Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections });

                var updateCircleResponse = await svc.UpdateCircleDefinition(circle);
                Assert.IsTrue(updateCircleResponse.IsSuccessStatusCode, $"Actual response {updateCircleResponse.StatusCode}");

                var updatedDefinitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(updatedDefinitionList);

                var circle2 = updatedDefinitionList.Single();


                Assert.AreEqual(circle.Name, circle2.Name);
                Assert.AreEqual(circle.Description, circle2.Description);
                CollectionAssert.AreEqual(circle.DriveGrants, circle2.DriveGrants);
                Assert.IsTrue(circle.Permissions == circle2.Permissions);

                await svc.DeleteCircleDefinition(circle.Id);

                //TODO: test that the changes to the drives and permissions were applied
            }
        }

        [Test]
        public async Task CanDisableCircle()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership })
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                var circle = definitionList.Single();
                Assert.IsTrue(circle.Permissions.HasKey(PermissionKeys.ReadCircleMembership));


                //
                circle.Disabled = true;

                var updateCircleResponse = await svc.UpdateCircleDefinition(circle);
                Assert.IsTrue(updateCircleResponse.IsSuccessStatusCode, $"Actual response {updateCircleResponse.StatusCode}");

                var updatedDefinitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(updatedDefinitionList);

                var updatedCircle = updatedDefinitionList.Single();

                Assert.AreEqual(updatedCircle.Disabled, true);

                Assert.AreEqual(updatedCircle.Name, circle.Name);
                Assert.AreEqual(updatedCircle.Description, circle.Description);
                CollectionAssert.AreEqual(updatedCircle.DriveGrants, circle.DriveGrants);
                Assert.IsTrue(updatedCircle.Permissions == circle.Permissions);

                await svc.DeleteCircleDefinition(circle.Id);

                //TODO: test that the changes to the drives and permissions were applied
            }
        }

        [Test]
        public async Task FailToUpdateInvalidCircle()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                var circle = definitionList.Single();

                //

                circle.Name = "updated name";
                circle.Description = "updated description";

                circle.DriveGrants = null;
                circle.Permissions = null;

                var updateCircleResponse = await svc.UpdateCircleDefinition(circle);
                Assert.IsTrue(updateCircleResponse.StatusCode == HttpStatusCode.InternalServerError, $"Actual response {updateCircleResponse.StatusCode}");

                await svc.DeleteCircleDefinition(circle.Id);
            }
        }

        [Test]
        public async Task CanDeleteCircle()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);
                var request = new CreateCircleRequest()
                {
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections })
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                var deleteCircleResponse = await svc.DeleteCircleDefinition(definitionList.Single().Id);
                Assert.IsTrue(deleteCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {deleteCircleResponse.StatusCode}");

                var secondGetCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(secondGetCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {secondGetCircleDefinitionsResponse.StatusCode}");
                var emptyDefinitionList = secondGetCircleDefinitionsResponse.Content;
                Assert.IsNotNull(emptyDefinitionList);
                Assert.IsTrue(!emptyDefinitionList.Any());
            }
        }
    }
}