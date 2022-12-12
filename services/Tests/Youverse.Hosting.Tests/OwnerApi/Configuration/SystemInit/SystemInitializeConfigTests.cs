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
using Youverse.Core.Services.Transit;
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
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
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

                var getIsIdentityConfiguredResponse1 = await svc.IsIdentityConfigured();
                Assert.IsTrue(getIsIdentityConfiguredResponse1.IsSuccessStatusCode);
                Assert.IsFalse(getIsIdentityConfiguredResponse1.Content);

                var setupConfig = new InitialSetupRequest()
                {
                    Drives = null,
                    Circles = null
                };

                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                var getIsIdentityConfiguredResponse = await svc.IsIdentityConfigured();
                Assert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
                Assert.IsTrue(getIsIdentityConfiguredResponse.Content);

                //
                // system drives should be created (neither allow anonymous)
                // 
                var driveSvc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);
                var createdDrivesResponse = await driveSvc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
                Assert.IsNotNull(createdDrivesResponse.Content);

                var createdDrives = createdDrivesResponse.Content;
                Assert.IsTrue(createdDrives.Results.Count == 4);

                Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.ContactDrive), $"expected drive [{SystemDriveConstants.ContactDrive}] not found");
                Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.ProfileDrive), $"expected drive [{SystemDriveConstants.ProfileDrive}] not found");
                Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.WalletDrive), $"expected drive [{SystemDriveConstants.WalletDrive}] not found");
                Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.ChatDrive), $"expected drive [{SystemDriveConstants.ChatDrive}] not found");

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
                Assert.IsTrue(systemCircle.DriveGrants.Count() == 2, "By default, there should be two drive grants (standard profile and chat drive)");
                
                Assert.IsNotNull(systemCircle.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive ==  SystemDriveConstants.ProfileDrive && dg.PermissionedDrive.Permission == DrivePermission.Read));
            
                //note: the permission for chat drive is write
                Assert.IsNotNull(systemCircle.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive ==  SystemDriveConstants.ChatDrive && dg.PermissionedDrive.Permission == DrivePermission.Write));
                Assert.IsTrue(!systemCircle.Permissions.Keys.Any(), "By default, the system circle should have no permissions");
            }
        }

        [Test]
        public async Task CanCreateSystemDrives_With_AdditionalDrivesAndCircles()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);
                
                var contactDrive = SystemDriveConstants.ContactDrive;
                var standardProfileDrive = SystemDriveConstants.ProfileDrive;
                    
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
                                Drive = standardProfileDrive,
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
                var expectedDrives = new List<TargetDrive>()
                {
                    standardProfileDrive,
                    contactDrive,
                    SystemDriveConstants.WalletDrive,
                    SystemDriveConstants.ChatDrive,
                    newDrive.TargetDrive
                };

                var driveSvc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);
                var createdDrivesResponse = await driveSvc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
                Assert.IsNotNull(createdDrivesResponse.Content);
                var createdDrives = createdDrivesResponse.Content;
                Assert.IsTrue(createdDrives.Results.Count == expectedDrives.Count);

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
                Assert.IsNotNull(standardProfileDriveGrant, "The standard profile drive should be in the system circle");

                //note: the permission for chat drive is write
                var chatDriveGrant =
                    systemCircle.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.ChatDrive && dg.PermissionedDrive.Permission == DrivePermission.Write);
                Assert.IsNotNull(chatDriveGrant, "the chat drive grant should exist in system circle");

                
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