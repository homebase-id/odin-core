using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.OwnerToken.Circles;
using Youverse.Hosting.Controllers.OwnerToken.Drive;
using Youverse.Hosting.Tests.OwnerApi.Circle;
using Youverse.Hosting.Tests.OwnerApi.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class SystemInitializeConfigTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: false);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanInitializeSystem_WithNoAdditionalDrives_and_NoAdditionalCircles()
        {
            //success = system drives created, other drives created
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);

                var setupConfig = new InitialSetupRequest()
                {
                    Drives = null,
                    Circles = null
                };

                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                //check if system drives exist
                var getSystemDrivesResponse = await svc.GetSystemDrives();
                Assert.IsNotNull(getSystemDrivesResponse.Content, "No system drives defined");
                Assert.IsTrue(getSystemDrivesResponse.IsSuccessStatusCode);
                var expectedDrives = getSystemDrivesResponse.Content.Values.Select(td => td).ToList();
                Assert.IsTrue(getSystemDrivesResponse.Content.TryGetValue("contact", out var contactDrive), "contact system drive should have returned");
                Assert.IsTrue(getSystemDrivesResponse.Content.TryGetValue("profile", out var standardProfileDrive), "standardProfileDrive should have returned");

                //
                // system drives should be created (neither allow anonymous)
                // 
                var driveSvc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);
                var createdDrivesResponse = await driveSvc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
                Assert.IsNotNull(createdDrivesResponse.Content);

                var createdDrives = createdDrivesResponse.Content;
                Assert.IsTrue(createdDrives.Results.Count == 2);

                foreach (var expectedDrive in expectedDrives)
                {
                    Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == expectedDrive), $"expected drive [{expectedDrive}] not found");
                }

                var circleDefinitionService = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var getCircleDefinitionsResponse = await circleDefinitionService.GetCircleDefinitions(includeSystemCircle: true);
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getCircleDefinitionsResponse.Content);
                var circleDefs = getCircleDefinitionsResponse.Content;

                Assert.IsTrue(circleDefs.Count() == 1, "Only the system circle should exist");

                var systemCircle = circleDefs.Single();
                Assert.IsTrue(systemCircle.Id == GuidId.FromString("we_are_connected"));
                Assert.IsTrue(systemCircle.Name == "System Circle");
                Assert.IsTrue(systemCircle.Description == "All Connected Identities");
                Assert.IsTrue(systemCircle.DriveGrants.Count() == 1, "By default, there should be one drive grant (standard profile drive allows anonymous)");
                Assert.IsNotNull(systemCircle.DriveGrants.Single(dg => dg.PermissionedDrive.Drive == standardProfileDrive && dg.PermissionedDrive.Permission == DrivePermission.Read));
                Assert.IsTrue(!systemCircle.Permissions.Keys.Any(), "By default, the system circle should have no permissions");
            }
        }

        [Test]
        public async Task CanCreateSystemDrives_With_AdditionalDrivesAndCircles()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);

                var getSystemDrivesResponse = await svc.GetSystemDrives();
                Assert.IsTrue(getSystemDrivesResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getSystemDrivesResponse.Content, "No system drives defined");

                var systemDrives = getSystemDrivesResponse.Content;
                Assert.IsTrue(systemDrives.TryGetValue("contact", out var contactDrive), "contact system drive should have returned");
                Assert.IsTrue(systemDrives.TryGetValue("profile", out var standardProfileDrive), "standardProfileDrive should have returned");

                var newDrive = new CreateDriveRequest()
                {
                    Name = "test",
                    AllowAnonymousReads = true,
                    Metadata = "",
                    TargetDrive = TargetDrive.NewTargetDrive()
                };

                var additionalCircleRequest = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "le circle",
                    Description = "an additional circle",
                    DriveGrants = new[]
                    {
                        new DriveGrantRequest()
                        {
                            PermissionedDrive = new PermissionedDrive()
                            {
                                Drive = contactDrive,
                                Permission = DrivePermission.Read
                            }
                        }
                    }
                };

                var setupConfig = new InitialSetupRequest()
                {
                    Drives = new List<CreateDriveRequest>() { newDrive },
                    Circles = new List<CreateCircleRequest>() { additionalCircleRequest }
                };

                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                //check if system drives exist
                var expectedDrives = systemDrives.Values.Select(td => td).ToList();
                expectedDrives.Add(newDrive.TargetDrive);

                var driveSvc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);
                var createdDrivesResponse = await driveSvc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
                Assert.IsNotNull(createdDrivesResponse.Content);
                var createdDrives = createdDrivesResponse.Content;
                Assert.IsTrue(createdDrives.Results.Count == 3);

                foreach (var expectedDrive in expectedDrives)
                {
                    Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == expectedDrive), $"expected drive [{expectedDrive}] not found");
                }

                var circleDefinitionService = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var getCircleDefinitionsResponse = await circleDefinitionService.GetCircleDefinitions(includeSystemCircle: true);
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getCircleDefinitionsResponse.Content);
                var circleDefs = getCircleDefinitionsResponse.Content.ToList();

                //
                // System circle exists and has correct grants
                //

                var systemCircle = circleDefs.SingleOrDefault(c => c.Id == CircleConstants.SystemCircleId);
                Assert.IsNotNull(systemCircle, "system circle should exist");
                Assert.IsTrue(systemCircle.Id == GuidId.FromString("we_are_connected"));
                Assert.IsTrue(systemCircle.Name == "System Circle");
                Assert.IsTrue(systemCircle.Description == "All Connected Identities");
                Assert.IsTrue(!systemCircle.Permissions.Keys.Any(), "By default, the system circle should have no permissions");

                var newDriveGrant = systemCircle.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == newDrive.TargetDrive && dg.PermissionedDrive.Permission == DrivePermission.Read);
                Assert.IsNotNull(newDriveGrant, "The new drive should be in the system circle");

                var standardProfileDriveGrant =
                    systemCircle.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == standardProfileDrive && dg.PermissionedDrive.Permission == DrivePermission.Read);
                Assert.IsNotNull(standardProfileDriveGrant, "The new drive should be in the system circle");

                //
                // additional circle exists
                //
                var additionalCircle = circleDefs.SingleOrDefault(c => c.Id == additionalCircleRequest.Id);
                Assert.IsNotNull(additionalCircle);
                Assert.IsTrue(additionalCircle.Name == "le circle");
                Assert.IsTrue(additionalCircle.Description == "an additional circle");
                Assert.IsTrue(additionalCircle.DriveGrants.Count(dg => dg.PermissionedDrive == additionalCircle.DriveGrants.Single().PermissionedDrive) == 1,
                    "The contact drive should be in the additional circle");
            }
        }

    }
}