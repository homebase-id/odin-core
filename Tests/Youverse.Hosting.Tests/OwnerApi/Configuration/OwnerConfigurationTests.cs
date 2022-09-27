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
                Assert.IsTrue(systemCircle.DriveGrants.Count() == 1, "By default, there should be one  drive grant (standard profile drive allows anonymous)");
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

        [Test]
        public async Task CanAllowConnectedContactsToViewConnections()
        {
            Assert.Inconclusive("TODO");

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
            Assert.Inconclusive("TODO");
        }

        [Test]
        public async Task CanAllowAnonymousToViewConnections()
        {
            Assert.Inconclusive("TODO");
        }

        [Test]
        public async Task CanBlockConnectedContactsFromViewingConnectionsUnlessInCircle()
        {
            Assert.Inconclusive("TODO");
        }

        [Test]
        public async Task CanBlockAuthenticatedVisitorsFromViewingConnections()
        {
            Assert.Inconclusive("TODO");
        }

        [Test]
        public async Task CanBlockAnonymousVisitorsFromViewingConnections()
        {
            Assert.Inconclusive("TODO");
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
                    Id = Guid.NewGuid(),
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