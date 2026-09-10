using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Hosting.Controllers.OwnerToken.Membership.Circles;
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
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise });
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }


        [SetUp]
        public void Setup()
        {
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
        }

        [Test, Explicit("TODO")]
        public void SystemCircleUpdatedWhenAnonymousDriveAdded()
        {
            Assert.Inconclusive("TODO");
        }

        [Test, Explicit("TODO")]
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
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

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
                ClassicAssert.IsTrue(createCircleResponse.StatusCode == HttpStatusCode.Forbidden, $"Failed.  Actual response {createCircleResponse.StatusCode}");
            }
        }


        [Test]
        public async Task FailToUpdateExistingCircleDefinitionByAddingOwnerOnlyDrive()
        {
            var identity = TestIdentities.Samwise;

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership })
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                ClassicAssert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                ClassicAssert.IsNotNull(definitionList);

                var circle = definitionList.Single(c => c.Id == request.Id);
                ClassicAssert.IsTrue(circle.Permissions.HasKey(PermissionKeys.ReadCircleMembership));

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
                ClassicAssert.IsTrue(updateCircleResponse.StatusCode == HttpStatusCode.Forbidden, $"Actual response {updateCircleResponse.StatusCode}");

                var getUpdatedCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                ClassicAssert.IsTrue(getUpdatedCircleDefinitionsResponse.IsSuccessStatusCode,
                    $"Failed.  Actual response {getUpdatedCircleDefinitionsResponse.StatusCode}");

                var updatedDefinitionList = getUpdatedCircleDefinitionsResponse.Content;
                ClassicAssert.IsNotNull(updatedDefinitionList);

                var circle2 = updatedDefinitionList.Single(c => c.Id == request.Id);


                ClassicAssert.AreNotEqual(circle.Name, circle2.Name);
                ClassicAssert.AreNotEqual(circle.Description, circle2.Description);
                CollectionAssert.AreNotEqual(circle.DriveGrants, circle2.DriveGrants);
                ClassicAssert.IsFalse(circle.Permissions == circle2.Permissions);

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
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var requestWithNoPermissionsOrDrives = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = new List<DriveGrantRequest>(),
                    Permissions = new PermissionSet()
                };

                var createCircleResponse = await svc.CreateCircleDefinition(requestWithNoPermissionsOrDrives);
                ClassicAssert.IsTrue(createCircleResponse.StatusCode == HttpStatusCode.BadRequest, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var code = TestUtils.ParseProblemDetails(createCircleResponse!.Error!);
                ClassicAssert.IsTrue(code == OdinClientErrorCode.AtLeastOneDriveOrPermissionRequiredForCircle);
            }
        }

        [Test]
        public async Task FailToCreateCircleWithUseTransitPermissions()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, TestIdentities.Frodo);
            var grant = new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new[] { PermissionKeys.UseTransitWrite })
            };

            var createCircleResponse = await client.Membership.CreateCircleRaw("Circle with UseTransit", grant);
            ClassicAssert.IsTrue(createCircleResponse.StatusCode == HttpStatusCode.BadRequest, $"Failed.  Actual response {createCircleResponse.StatusCode}");
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
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

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
                ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                ClassicAssert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                ClassicAssert.IsNotNull(definitionList);

                var circle = definitionList.Single(c => c.Id == request.Id);

                // ClassicAssert.IsNotNull(circle.DrivesGrants.SingleOrDefault(d => d.Drive.Alias == dgr1.Drive.Alias && d.Drive.Type == dgr1.Drive.Type && d.Permission == dgr1.Permission));
                // ClassicAssert.IsNotNull(circle.DrivesGrants.SingleOrDefault(d => d.Drive.Alias == dgr2.Drive.Alias && d.Drive.Type == dgr2.Drive.Type && d.Permission == dgr2.Permission));

                ClassicAssert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d.PermissionedDrive == dgr1.PermissionedDrive));
                ClassicAssert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d.PermissionedDrive == dgr1.PermissionedDrive));

                ClassicAssert.IsTrue(circle.Permissions.HasKey(PermissionKeys.ReadCircleMembership));
                ClassicAssert.IsTrue(circle.Permissions.HasKey(PermissionKeys.ReadConnections));

                ClassicAssert.AreEqual(request.Name, circle.Name);
                ClassicAssert.AreEqual(request.Description, circle.Description);
                // CollectionAssert.AreEquivalent(request.Drives, circle.Drives);
                ClassicAssert.IsTrue(request.Permissions == circle.Permissions);

                // cleanup
                await svc.DeleteCircleDefinition(circle.Id);
            }
        }

        [Test]
        public async Task CanGetListOfCircleDefinitions()
        {
            var request1 = new CreateCircleRequest()
            {
                Id = Guid.NewGuid(),
                Name = "Test Circle 1",
                Description = "Test circle description 1",
                DriveGrants = null,
                Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership })
            };

            var request2 = new CreateCircleRequest()
            {
                Id = Guid.NewGuid(),
                Name = "Test Circle 2",
                Description = "Test circle description 2",
                DriveGrants = null,
                Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections })
            };

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);
                try
                {
                    var createCircleResponse1 = await svc.CreateCircleDefinition(request1);
                    ClassicAssert.IsTrue(createCircleResponse1.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse1.StatusCode}");

                    var createCircleResponse2 = await svc.CreateCircleDefinition(request2);
                    ClassicAssert.IsTrue(createCircleResponse2.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse2.StatusCode}");

                    var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                    ClassicAssert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                    ClassicAssert.IsNotNull(getCircleDefinitionsResponse.Content);
                    var definitionList = getCircleDefinitionsResponse.Content.ToList();

                    // The owner's list is the two circles made here plus every circle the app tree
                    // provisions. Counted from the tree rather than written down, so adding an app's
                    // circle does not silently break an unrelated assertion.
                    var seeded = BuiltinApps.SeededCircles.Select(c => (Guid)c.Id).Distinct().Count();
                    ClassicAssert.IsTrue(definitionList.Count == 2 + seeded,
                        $"expected {2 + seeded} circles, found {definitionList.Count}");

                    var circle1 = definitionList.Single(x => x.Name == request1.Name);
                    ClassicAssert.AreEqual(request1.Name, circle1.Name);
                    ClassicAssert.AreEqual(request1.Description, circle1.Description);
                    CollectionAssert.AreEqual(request1.DriveGrants, circle1.DriveGrants);
                    ClassicAssert.IsTrue(request1.Permissions == circle1.Permissions);

                    var circle2 = definitionList.Single(x => x.Name == request2.Name);
                    ClassicAssert.AreEqual(request2.Name, circle2.Name);
                    ClassicAssert.AreEqual(request2.Description, circle2.Description);
                    CollectionAssert.AreEqual(request2.DriveGrants, circle2.DriveGrants);
                    ClassicAssert.IsTrue(request2.Permissions == circle2.Permissions);
                }
                finally
                {
                    // cleanup
                    await svc.DeleteCircleDefinition(request1.Id);
                    await svc.DeleteCircleDefinition(request2.Id);
                }

            }
        }

        [Test]
        public async Task CanUpdateCircleDefinition_NoMembershipReconciliation()
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership })
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                ClassicAssert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                ClassicAssert.IsNotNull(definitionList);

                var circle = definitionList.Single(c => c.Id == request.Id);
                ClassicAssert.IsTrue(circle.Permissions.HasKey(PermissionKeys.ReadCircleMembership));


                //

                circle.Name = "updated name";
                circle.Description = "updated description";

                circle.DriveGrants = null;
                circle.Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections });

                var updateCircleResponse = await svc.UpdateCircleDefinition(circle);
                ClassicAssert.IsTrue(updateCircleResponse.IsSuccessStatusCode, $"Actual response {updateCircleResponse.StatusCode}");

                var getUpdatedCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                ClassicAssert.IsTrue(getUpdatedCircleDefinitionsResponse.IsSuccessStatusCode,
                    $"Failed.  Actual response {getUpdatedCircleDefinitionsResponse.StatusCode}");

                var updatedDefinitionList = getUpdatedCircleDefinitionsResponse.Content;
                ClassicAssert.IsNotNull(updatedDefinitionList);

                var circle2 = updatedDefinitionList.Single(c => c.Id == request.Id);


                ClassicAssert.AreEqual(circle.Name, circle2.Name);
                ClassicAssert.AreEqual(circle.Description, circle2.Description);
                CollectionAssert.AreEqual(circle.DriveGrants, circle2.DriveGrants);
                ClassicAssert.IsTrue(circle.Permissions == circle2.Permissions);

                await svc.DeleteCircleDefinition(circle.Id);

                //TODO: test that the changes to the drives and permissions were applied
            }
        }

        [Test]
        public async Task CanDisableCircle()
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership })
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                ClassicAssert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                ClassicAssert.IsNotNull(definitionList);

                var circle = definitionList.Single(c => c.Id == request.Id);
                ClassicAssert.IsTrue(circle.Permissions.HasKey(PermissionKeys.ReadCircleMembership));


                //
                circle.Disabled = true;

                var updateCircleResponse = await svc.UpdateCircleDefinition(circle);
                ClassicAssert.IsTrue(updateCircleResponse.IsSuccessStatusCode, $"Actual response {updateCircleResponse.StatusCode}");

                var updatedDefinitionList = getCircleDefinitionsResponse.Content;
                ClassicAssert.IsNotNull(updatedDefinitionList);

                var updatedCircle = updatedDefinitionList.Single(c => c.Id == request.Id);

                ClassicAssert.AreEqual(updatedCircle.Disabled, true);

                ClassicAssert.AreEqual(updatedCircle.Name, circle.Name);
                ClassicAssert.AreEqual(updatedCircle.Description, circle.Description);
                CollectionAssert.AreEqual(updatedCircle.DriveGrants, circle.DriveGrants);
                ClassicAssert.IsTrue(updatedCircle.Permissions == circle.Permissions);

                await svc.DeleteCircleDefinition(circle.Id);

                //TODO: test that the changes to the drives and permissions were applied
            }
        }

        [Test]
        public async Task FailToUpdateInvalidCircle()
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                ClassicAssert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                ClassicAssert.IsNotNull(definitionList);

                var circle = definitionList.Single(c => c.Id == request.Id);

                //

                circle.Name = "updated name";
                circle.Description = "updated description";

                circle.DriveGrants = null;
                circle.Permissions = null;

                var updateCircleResponse = await svc.UpdateCircleDefinition(circle);

                ClassicAssert.IsTrue(updateCircleResponse.StatusCode == HttpStatusCode.BadRequest, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var code = TestUtils.ParseProblemDetails(updateCircleResponse.Error!);
                ClassicAssert.IsTrue(code == OdinClientErrorCode.AtLeastOneDriveOrPermissionRequiredForCircle);

                await svc.DeleteCircleDefinition(circle.Id);
            }
        }

        [Test]
        public async Task CanDeleteCircle()
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);
                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Circle",
                    Description = "Test circle description",
                    DriveGrants = null,
                    Permissions = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections })
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                ClassicAssert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                ClassicAssert.IsNotNull(definitionList);

                var id = definitionList.Single(c => c.Id == request.Id).Id;
                var deleteCircleResponse = await svc.DeleteCircleDefinition(id);
                ClassicAssert.IsTrue(deleteCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {deleteCircleResponse.StatusCode}");

                var secondGetCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                ClassicAssert.IsTrue(secondGetCircleDefinitionsResponse.IsSuccessStatusCode,
                    $"Failed.  Actual response {secondGetCircleDefinitionsResponse.StatusCode}");
                var remainingDefinitionList = secondGetCircleDefinitionsResponse.Content;
                ClassicAssert.IsNotNull(remainingDefinitionList);
                // The deleted user circle is gone; only built-in circles remain.
                ClassicAssert.IsFalse(remainingDefinitionList.Any(c => c.Id == request.Id));
            }
        }

        //
        // Adoption: giving an unowned circle an owning app.
        //
        // A circle with no AppId reads as the owner's own, which is right for most and wrong for the
        // ones that predate app ownership.  Adoption is one-way -- it fills an empty owner, never moves
        // a set one -- because PendingEnrollment denormalises AppId on the promise that ownership does
        // not change.
        //

        [Test]
        public async Task AdoptingAnUnownedCircleGivesItTheApp()
        {
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var appId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(appId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Circle awaiting an owner",
                    Description = "Created without an app, as everything before app ownership was",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                var create = await svc.CreateCircleDefinition(request);
                ClassicAssert.IsTrue(create.IsSuccessStatusCode, $"Failed.  Actual response {create.StatusCode}");

                var before = (await svc.GetCircleDefinition(request.Id)).Content;
                Assert.That(before, Is.Not.Null);
                Assert.That(before.AppId, Is.Null, "a circle created without an app must start unowned");

                var adopt = await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = appId
                });
                Assert.That(adopt.IsSuccessStatusCode, Is.True, $"Failed.  Actual response {adopt.StatusCode}");

                var after = (await svc.GetCircleDefinition(request.Id)).Content;
                Assert.That(after, Is.Not.Null);
                Assert.That(after.AppId, Is.EqualTo(appId));

                // Adoption names an administrator.  Everything the circle actually grants is untouched.
                Assert.That(after.Name, Is.EqualTo(before.Name));
                Assert.That(after.Description, Is.EqualTo(before.Description));
                Assert.That(after.Permissions.HasKey(PermissionKeys.ReadCircleMembership), Is.True);
                Assert.That(after.GrantOn, Is.EqualTo(before.GrantOn));
                Assert.That(after.Designation, Is.EqualTo(before.Designation));
                Assert.That(after.Emoji, Is.EqualTo(before.Emoji));
                Assert.That(after.Disabled, Is.EqualTo(before.Disabled));
                Assert.That(after.Created, Is.EqualTo(before.Created));
            }
        }

        [Test]
        public async Task AdoptingACircleThatAlreadyHasAnAppIsRefused()
        {
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var firstAppId = Guid.NewGuid();
            var secondAppId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(firstAppId, new PermissionSetGrantRequest());
            await ownerClient.Apps.RegisterApp(secondAppId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Circle adopted once",
                    Description = "Ownership is set once and never moved",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                ClassicAssert.IsTrue((await svc.CreateCircleDefinition(request)).IsSuccessStatusCode);
                ClassicAssert.IsTrue((await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = firstAppId
                })).IsSuccessStatusCode);

                var second = await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = secondAppId
                });

                Assert.That(second.IsSuccessStatusCode, Is.False, "ownership must not be reassignable");
                Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

                var after = (await svc.GetCircleDefinition(request.Id)).Content;
                Assert.That(after!.AppId, Is.EqualTo(firstAppId), "the refused call must not have moved it");
            }
        }

        [Test]
        public async Task ReAdoptingByTheSameAppIsRefused()
        {
            // Not idempotent on purpose: a repeat is indistinguishable from two apps racing for the
            // circle, and that is the reading worth failing on.
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var appId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(appId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Circle adopted twice by one app",
                    Description = "",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                ClassicAssert.IsTrue((await svc.CreateCircleDefinition(request)).IsSuccessStatusCode);

                var body = new SetCircleOwningAppRequest { CircleId = request.Id, AppId = appId };
                ClassicAssert.IsTrue((await svc.SetCircleOwningApp(body)).IsSuccessStatusCode);

                var again = await svc.SetCircleOwningApp(body);
                Assert.That(again.IsSuccessStatusCode, Is.False);
                Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            }
        }

        [Test]
        public async Task AdoptingByAnUnregisteredAppIsRefused()
        {
            // Checked before the write: stamping an app that does not exist would leave the circle in
            // the very state adoption exists to escape -- owned by something that can never claim it,
            // and no longer adoptable.
            var identity = TestIdentities.Samwise;

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var request = new CreateCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Circle offered to nobody",
                    Description = "",
                    Permissions = new PermissionSet(new List<int> { PermissionKeys.ReadCircleMembership })
                };

                ClassicAssert.IsTrue((await svc.CreateCircleDefinition(request)).IsSuccessStatusCode);

                var response = await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = request.Id,
                    AppId = Guid.NewGuid()
                });

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

                var after = (await svc.GetCircleDefinition(request.Id)).Content;
                Assert.That(after!.AppId, Is.Null, "the circle must still be adoptable");
            }
        }

        [Test]
        public async Task AdoptingACircleThatDoesNotExistIsRefused()
        {
            var identity = TestIdentities.Samwise;
            var ownerClient = new OwnerApiClient(_scaffold.OldOwnerApi, identity);

            var appId = Guid.NewGuid();
            await ownerClient.Apps.RegisterApp(appId, new PermissionSetGrantRequest());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

                var response = await svc.SetCircleOwningApp(new SetCircleOwningAppRequest
                {
                    CircleId = Guid.NewGuid(),
                    AppId = appId
                });

                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            }
        }
    }
}
