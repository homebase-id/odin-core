using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Membership.Circles
{
    public class CircleDefinitionTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
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
        public void SystemCircleUpdatedWhenAnonymousDriveAdded()
        {
            Assert.Inconclusive("TODO");
        }

        [Test]
        public void SystemCircleUpdatedWhenAnonymousDriveRemoved()
        {
            Assert.Inconclusive("TODO");
        }


        [Test]
        public async Task FailToCreateCircleWithOwnerOnlyDrive()
        {
            var targetDrive1 = TargetDrive.NewTargetDrive();

            var identity = TestIdentities.Samwise;
            await _scaffold.OldOwnerApi.CreateDrive(identity.OdinId, targetDrive1, "Owner Only for Circle Test", "", false, true);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity.OdinId, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var requestWithOwnerOnlyDrive = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = new List<DriveGrantRequest>()
                    {
                        new()
                        {
                            PermissionedDrive = new()
                            {
                                Drive = targetDrive1,
                                Permission = DrivePermission.Read
                            }
                        }
                    },
                    Permissions = new PermissionSet()
                };

                var createCircleResponse = await svc.CreateCircleDefinition(requestWithOwnerOnlyDrive);
                Assert.IsTrue(createCircleResponse.StatusCode == HttpStatusCode.Forbidden, $"Failed.  Actual response {createCircleResponse.StatusCode}");
            }
        }


        [Test]
        public async Task FailToUpdateExistingCircleDefinitionByAddingOwnerOnlyDrive()
        {
            var identity = TestIdentities.Samwise;

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
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

                //Add an owner-only drive

                var targetDrive1 = TargetDrive.NewTargetDrive();
                await _scaffold.OldOwnerApi.CreateDrive(identity.OdinId, targetDrive1, "Owner Only for Circle Test", "", false, true);


                //

                circle.Name = "updated name";
                circle.Description = "updated description";

                circle.DriveGrants = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = targetDrive1,
                            Permission = DrivePermission.Read
                        }
                    }
                };

                circle.Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections });

                var updateCircleResponse = await svc.UpdateCircleDefinition(circle);
                Assert.IsTrue(updateCircleResponse.StatusCode == HttpStatusCode.Forbidden, $"Actual response {updateCircleResponse.StatusCode}");

                var getUpdatedCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getUpdatedCircleDefinitionsResponse.IsSuccessStatusCode,
                    $"Failed.  Actual response {getUpdatedCircleDefinitionsResponse.StatusCode}");

                var updatedDefinitionList = getUpdatedCircleDefinitionsResponse.Content;
                Assert.IsNotNull(updatedDefinitionList);

                var circle2 = updatedDefinitionList.Single();


                Assert.AreNotEqual(circle.Name, circle2.Name);
                Assert.AreNotEqual(circle.Description, circle2.Description);
                CollectionAssert.AreNotEqual(circle.DriveGrants, circle2.DriveGrants);
                Assert.IsFalse(circle.Permissions == circle2.Permissions);

                await svc.DeleteCircleDefinition(circle.Id);

                //TODO: test that the changes to the drives and permissions were applied
            }
        }


        [Test]
        public async Task FailToCreateInvalidCircle()
        {
            var targetDrive1 = TargetDrive.NewTargetDrive();
            var targetDrive2 = TargetDrive.NewTargetDrive();

            var identity = TestIdentities.Samwise;
            await _scaffold.OldOwnerApi.CreateDrive(identity.OdinId, targetDrive1, "Drive 1 for Circle Test", "", false);
            await _scaffold.OldOwnerApi.CreateDrive(identity.OdinId, targetDrive2, "Drive 2 for Circle Test", "", false);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity.OdinId, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var requestWithNoPermissionsOrDrives = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = new List<DriveGrantRequest>(),
                    Permissions = new PermissionSet()
                };

                var createCircleResponse = await svc.CreateCircleDefinition(requestWithNoPermissionsOrDrives);
                Assert.IsTrue(createCircleResponse.StatusCode == HttpStatusCode.BadRequest, $"Failed.  Actual response {createCircleResponse.StatusCode}");
                Assert.IsTrue(
                    int.TryParse(OdinSystemSerializer.Deserialize<ProblemDetails>(createCircleResponse!.Error!.Content!)!.Extensions["errorCode"].ToString(),
                        out var code),
                    "Could not parse problem result");
                Assert.IsTrue(code == (int)OdinClientErrorCode.AtLeastOneDriveOrPermissionRequiredForCircle);
            }
        }

        [Test]
        public async Task FailToCreateCircleWithUseTransitPermissions()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, TestIdentities.Frodo);
            var grant = new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new[] { PermissionKeys.UseTransit })
            };
            
            var createCircleResponse = await client.Membership.CreateCircleRaw("Circle with UseTransit", grant);
            Assert.IsTrue(createCircleResponse.StatusCode == HttpStatusCode.BadRequest, $"Failed.  Actual response {createCircleResponse.StatusCode}");
        }

        [Test]
        public async Task CanCreateCircle()
        {
            var targetDrive1 = TargetDrive.NewTargetDrive();
            var targetDrive2 = TargetDrive.NewTargetDrive();

            var identity = TestIdentities.Samwise;
            await _scaffold.OldOwnerApi.CreateDrive(identity.OdinId, targetDrive1, "Drive 1 for Circle Test", "", false);
            await _scaffold.OldOwnerApi.CreateDrive(identity.OdinId, targetDrive2, "Drive 2 for Circle Test", "", false);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
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
                    Id = Guid.NewGuid(),
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
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var request1 = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Circle 1",
                    Description = "Test circle description 1",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership })
                };

                var createCircleResponse1 = await svc.CreateCircleDefinition(request1);
                Assert.IsTrue(createCircleResponse1.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse1.StatusCode}");


                var request2 = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
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
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
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

                var getUpdatedCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getUpdatedCircleDefinitionsResponse.IsSuccessStatusCode,
                    $"Failed.  Actual response {getUpdatedCircleDefinitionsResponse.StatusCode}");

                var updatedDefinitionList = getUpdatedCircleDefinitionsResponse.Content;
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
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
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
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
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

                Assert.IsTrue(updateCircleResponse.StatusCode == HttpStatusCode.BadRequest, $"Failed.  Actual response {createCircleResponse.StatusCode}");
                Assert.IsTrue(
                    int.TryParse(OdinSystemSerializer.Deserialize<ProblemDetails>(updateCircleResponse!.Error!.Content!)!.Extensions["errorCode"].ToString(),
                        out var code),
                    "Could not parse problem result");
                Assert.IsTrue(code == (int)OdinClientErrorCode.AtLeastOneDriveOrPermissionRequiredForCircle);

                await svc.DeleteCircleDefinition(circle.Id);
            }
        }

        [Test]
        public async Task CanDeleteCircle()
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);
                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
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

                var id = definitionList.Single().Id;
                var deleteCircleResponse = await svc.DeleteCircleDefinition(id);
                Assert.IsTrue(deleteCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {deleteCircleResponse.StatusCode}");

                var secondGetCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(secondGetCircleDefinitionsResponse.IsSuccessStatusCode,
                    $"Failed.  Actual response {secondGetCircleDefinitionsResponse.StatusCode}");
                var emptyDefinitionList = secondGetCircleDefinitionsResponse.Content;
                Assert.IsNotNull(emptyDefinitionList);
                Assert.IsTrue(!emptyDefinitionList.Any());
            }
        }
    }
}