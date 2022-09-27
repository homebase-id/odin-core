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
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Provisioning;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.OwnerToken.Circles;
using Youverse.Hosting.Controllers.OwnerToken.Drive;
using Youverse.Hosting.Tests.OwnerApi.Circle;
using Youverse.Hosting.Tests.OwnerApi.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Configuration
{
    public class OwnerConfigurationTests
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
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
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

                var getCircleDefinitionsResponse = await circleDefinitionService.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getCircleDefinitionsResponse.Content);
                var circleDefs = getCircleDefinitionsResponse.Content;

                Assert.IsTrue(circleDefs.Count() == 1, "Only the system circle should exist");

                var systemCircle = circleDefs.Single();
                Assert.IsTrue(systemCircle.Id == GuidId.FromString("we_are_connected"));
                Assert.IsTrue(systemCircle.Name == "System Circle");
                Assert.IsTrue(systemCircle.Description == "All Connected Identities");
                Assert.IsTrue(!systemCircle.DriveGrants.Any(), "By default, there should be no drive grants because none of the system drives allow anonymous");
                Assert.IsTrue(!systemCircle.Permissions.Keys.Any(), "By default, the system circle should have no permissions");
            }
        }

        [Test]
        public async Task CanCreateSystemDrives_And_AdditionalDrives_NoAdditionalCircles()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);

                var newDrive = new CreateDriveRequest()
                {
                    Name = "test",
                    AllowAnonymousReads = true,
                    Metadata = "",
                    TargetDrive = TargetDrive.NewTargetDrive()
                };

                var setupConfig = new InitialSetupRequest()
                {
                    Drives = new List<CreateDriveRequest>() { newDrive },
                    Circles = null
                };

                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                //check if system drives exist
                var getSystemDrivesResponse = await svc.GetSystemDrives();
                Assert.IsNotNull(getSystemDrivesResponse.Content, "No system drives defined");
                Assert.IsTrue(getSystemDrivesResponse.IsSuccessStatusCode);
                var expectedDrives = getSystemDrivesResponse.Content.Values.Select(td => td).ToList();
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

                var getCircleDefinitionsResponse = await circleDefinitionService.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getCircleDefinitionsResponse.Content);
                var circleDefs = getCircleDefinitionsResponse.Content;

                Assert.IsTrue(circleDefs.Count(c=>c.Id == CircleConstants.SystemCircleId) == 1, "Only the system circle should exist");

                var systemCircle = circleDefs.Single();
                Assert.IsTrue(systemCircle.Id == GuidId.FromString("we_are_connected"));
                Assert.IsTrue(systemCircle.Name == "System Circle");
                Assert.IsTrue(systemCircle.Description == "All Connected Identities");
                Assert.IsTrue(!systemCircle.Permissions.Keys.Any(), "By default, the system circle should have no permissions");
                Assert.IsTrue(systemCircle.DriveGrants.Count(dg => dg.PermissionedDrive.Drive == newDrive.TargetDrive && dg.PermissionedDrive.Permission == DrivePermission.Read) == 1,
                    "There should be one drive in the system circle due to the 'newDrive' with allowAnonymous=true added above");

                //take out the anonymous drive so future tests have a chance
                var setDriveModeResponse = await driveSvc.SetDriveReadMode(new UpdateDriveDefinitionRequest()
                {
                    TargetDrive = newDrive.TargetDrive,
                    AllowAnonymousReads = false
                });

                Assert.IsTrue(setDriveModeResponse.IsSuccessStatusCode, "Failed setting drive mode");
            }
        }

        [Test]
        public async Task CanCreateSystemCircle_And_AdditionalCircles_NoAdditionalDrives()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);

                var getSystemDrivesResponse = await svc.GetSystemDrives();
                Assert.IsNotNull(getSystemDrivesResponse.Content, "No system drives defined");
                Assert.IsTrue(getSystemDrivesResponse.IsSuccessStatusCode);
                Assert.IsTrue(getSystemDrivesResponse.Content.TryGetValue("contact", out var contactDrive), "contact system drive should have returned");
                Assert.IsTrue(getSystemDrivesResponse.Content.TryGetValue("profile", out var standardProfileDrive), "standardProfileDrive should have returned");
                
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
                    Drives = null,
                    Circles = new List<CreateCircleRequest>() { additionalCircleRequest }
                };

                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                var driveSvc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);
                var createdDrivesResponse = await driveSvc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
                Assert.IsNotNull(createdDrivesResponse.Content);
                var createdDrives = createdDrivesResponse.Content;
                Assert.IsTrue(createdDrives.Results.Count == 2);

                var expectedDrives = getSystemDrivesResponse.Content.Values.Select(td => td).ToList();
                foreach (var expectedDrive in expectedDrives)
                {
                    Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == expectedDrive), $"expected drive [{expectedDrive}] not found");
                }

                var circleDefinitionService = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var getCircleDefinitionsResponse = await circleDefinitionService.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getCircleDefinitionsResponse.Content);
                var circleDefs = getCircleDefinitionsResponse.Content;

                Assert.IsTrue(circleDefs.Count() == 2, "Only the system circle should exist");

                //
                // system circle exists
                //
                var systemCircle = circleDefs.SingleOrDefault(c => c.Id == CircleConstants.SystemCircleId);
                Assert.IsNotNull(systemCircle);
                Assert.IsTrue(systemCircle.Id == GuidId.FromString("we_are_connected"));
                Assert.IsTrue(systemCircle.Name == "System Circle");
                Assert.IsTrue(systemCircle.Description == "All Connected Identities");
                Assert.IsTrue(!systemCircle.Permissions.Keys.Any(), "By default, the system circle should have no permissions");


                var standardProfileDriveGrant = systemCircle.DriveGrants.SingleOrDefault();
                Assert.IsNotNull(standardProfileDriveGrant);
                Assert.IsTrue(standardProfileDriveGrant.PermissionedDrive.Drive == standardProfileDrive && standardProfileDriveGrant.PermissionedDrive.Permission == DrivePermission.Read);
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

        [Test]
        public async Task CanAllowConnectedContactsToViewConnections()
        {
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();
            //
            // using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
            // {
            //     var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);
            //
            //     var header = new AcceptRequestHeader()
            //     {
            //         Sender = frodo.Identity,
            //         CircleIds = new List<GuidId>(),
            //         ContactData = sam.ContactData
            //     };
            //
            //     var acceptResponse = await svc.AcceptConnectionRequest(header);
            //
            //     Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");
            //
            //     await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Connected);
            //
            //     var samConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            //     var blockResponse = await samConnections.Block(new DotYouIdRequest() { DotYouId = frodo.Identity });
            //
            //     Assert.IsTrue(blockResponse.IsSuccessStatusCode && blockResponse.Content, "failed to block");
            //     await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Blocked);
            //
            //     await samConnections.Unblock(new DotYouIdRequest() { DotYouId = frodo.Identity });
            // }

            await DisconnectIdentities(frodo, sam);
        }

        [Test]
        public async Task CanAllowAuthenticatedVisitorsToViewConnections()
        {
        }

        [Test]
        public async Task CanAllowAnonymousToViewConnections()
        {
        }

        [Test]
        public async Task CanBlockConnectedContactsFromViewingConnectionsUnlessInCircle()
        {
        }

        [Test]
        public async Task CanBlockAuthenticatedVisitorsFromViewingConnections()
        {
        }

        [Test]
        public async Task CanBlockAnonymousVisitorsFromViewingConnections()
        {
        }


        private void AssertAllDrivesGrantedFromCircle(CircleDefinition circleDefinition, RedactedCircleGrant actual)
        {
            foreach (var circleDriveGrant in circleDefinition.DriveGrants)
            {
                //be sure it's in the list of granted drives; use Single to be sure it's only in there once
                var result = actual.DriveGrants.SingleOrDefault(x =>
                    x.PermissionedDrive.Drive == circleDriveGrant.PermissionedDrive.Drive && x.PermissionedDrive.Permission == circleDriveGrant.PermissionedDrive.Permission);
                Assert.NotNull(result);
            }
        }

        private async Task AssertIdentityIsInCircle(HttpClient client, SensitiveByteArray ownerSharedSecret, GuidId circleId, DotYouIdentity expectedIdentity)
        {
            var circleMemberSvc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            var getCircleMemberResponse = await circleMemberSvc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = circleId });
            Assert.IsTrue(getCircleMemberResponse.IsSuccessStatusCode, $"Actual status code {getCircleMemberResponse.StatusCode}");
            var members = getCircleMemberResponse.Content;
            Assert.NotNull(members);
            Assert.IsTrue(members.Any());
            Assert.IsFalse(members.SingleOrDefault(m => m == expectedIdentity).Id == null);
        }

        private async Task AssertConnectionStatus(HttpClient client, SensitiveByteArray ownerSharedSecret, string dotYouId, ConnectionStatus expected)
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            var response = await svc.GetConnectionInfo(new DotYouIdRequest() { DotYouId = dotYouId });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {dotYouId}.  Status code was {response.StatusCode}");
            Assert.IsNotNull(response.Content, $"No status for {dotYouId} found");
            Assert.IsTrue(response.Content.Status == expected, $"{dotYouId} status does not match {expected}");
        }

        private async Task<(TestSampleAppContext, TestSampleAppContext, ConnectionRequestHeader)> CreateConnectionRequestFrodoToSam(CircleDefinition circleDefinition1 = null,
            CircleDefinition circleDefinition2 = null)
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canReadConnections: true);
            var recipient = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canReadConnections: true);

            List<GuidId> cids = new List<GuidId>();
            if (null != circleDefinition1)
            {
                cids.Add(circleDefinition1.Id);
            }

            if (null != circleDefinition2)
            {
                cids.Add(circleDefinition2.Id);
            }

            var id = Guid.NewGuid();
            var requestHeader = new ConnectionRequestHeader()
            {
                Id = id,
                Recipient = recipient.Identity,
                Message = "Please add me",
                CircleIds = cids,
                ContactData = sender.ContactData
            };

            //have frodo send it
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sender.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var response = await svc.SendConnectionRequest(requestHeader);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response.Content, "Failed sending the request");
            }

            //check that sam got it
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);
                var response = await svc.GetPendingRequest(new DotYouIdRequest() { DotYouId = sender.Identity });

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
                Assert.IsTrue(response.Content.SenderDotYouId == sender.Identity);
            }

            return (sender, recipient, requestHeader);
        }

        private async Task DeleteConnectionRequestsFromFrodoToSam(TestSampleAppContext frodo, TestSampleAppContext sam)
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var deleteResponse = await svc.DeletePendingRequest(new DotYouIdRequest() { DotYouId = frodo.Identity });
                Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(new DotYouIdRequest() { DotYouId = sam.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
            }

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var deleteResponse = await svc.DeleteSentRequest(new DotYouIdRequest() { DotYouId = sam.Identity });
                Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(new DotYouIdRequest() { DotYouId = sam.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
            }
        }

        private async Task DisconnectIdentities(TestSampleAppContext frodo, TestSampleAppContext sam)
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
            {
                var frodoConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var disconnectResponse = await frodoConnections.Disconnect(new DotYouIdRequest() { DotYouId = sam.Identity });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Samwise.DotYouId, ConnectionStatus.None);
            }

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
            {
                var samConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var disconnectResponse = await samConnections.Disconnect(new DotYouIdRequest() { DotYouId = frodo.Identity });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Frodo.DotYouId, ConnectionStatus.None);
            }
        }

        private async Task<CircleDefinition> CreateCircleWith2Drives(DotYouIdentity identity, string name, IEnumerable<int> permissionKeys)
        {
            var targetDrive1 = TargetDrive.NewTargetDrive();
            var targetDrive2 = TargetDrive.NewTargetDrive();

            await _scaffold.OwnerApi.CreateDrive(identity, targetDrive1, $"Drive 1 for circle {name}", "", false);
            await _scaffold.OwnerApi.CreateDrive(identity, targetDrive2, $"Drive 2 for circle {name}", "", false);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                Guid someId = Guid.NewGuid();
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
                        Drive = targetDrive2,
                        Permission = DrivePermission.Write
                    }
                };

                var request = new CreateCircleRequest()
                {
                    Name = name,
                    Description = $"total hack {someId}",
                    DriveGrants = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    Permissions = permissionKeys?.Any() ?? false ? new PermissionSet(permissionKeys?.ToArray()) : new PermissionSet()
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                //grab the circle by the id we put in the description.  we don't have the newly created circle's id because i need to update the create circle method  
                var circle = definitionList.Single(c => c.Description.Contains(someId.ToString()));

                Assert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d == dgr1));
                Assert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d == dgr2));

                foreach (var k in permissionKeys)
                {
                    Assert.IsTrue(circle.Permissions.HasKey(k));
                }

                Assert.AreEqual(request.Name, circle.Name);
                Assert.AreEqual(request.Description, circle.Description);
                Assert.IsTrue(request.Permissions == circle.Permissions);

                return circle;
            }
        }
    }
}