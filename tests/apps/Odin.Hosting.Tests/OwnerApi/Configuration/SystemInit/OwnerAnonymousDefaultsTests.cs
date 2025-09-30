using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests.YouAuthApi.Circle;
using Odin.Services.Configuration;
using Refit;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace Odin.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class OwnerAnonymousDefaultsTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: false, testIdentities: new List<TestIdentity>() { TestIdentities.Pippin, TestIdentities.Merry, TestIdentities.Frodo, TestIdentities.Samwise });
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


        [Test]
        public async Task CanAllowAnonymousToViewConnections()
        {
            var utils = new ConfigurationTestUtilities(_scaffold);
            
            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

            var frodoInitResponse = await frodoOwnerClient.Configuration.InitializeIdentity(new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });
            
            ClassicAssert.IsTrue(frodoInitResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(frodoInitResponse.Content);
    
            var samInitResponse = await samOwnerClient.Configuration.InitializeIdentity( new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });
            
            ClassicAssert.IsTrue(samInitResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(samInitResponse.Content);

            var (frodo, sam, _) = await utils.CreateConnectionRequestFrodoToSam();
            await utils.AcceptConnectionRequest(sender: frodo, recipient: sam);

            var client = _scaffold.CreateAnonymousApiHttpClient(samOwnerClient.Identity.OdinId);
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);
                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, "");
                ClassicAssert.IsTrue(getConnectionsResponse.StatusCode == HttpStatusCode.Forbidden, "Should have failed to get connections with 403 status code.");
            }

            await samOwnerClient.Configuration.UpdateTenantSettingsFlag(TenantConfigFlagNames.AnonymousVisitorsCanViewConnections, bool.TrueString);

            client = _scaffold.CreateAnonymousApiHttpClient(samOwnerClient.Identity.OdinId);
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);

                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, "");
                ClassicAssert.IsTrue(getConnectionsResponse.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(getConnectionsResponse.Content);
                ClassicAssert.IsTrue(getConnectionsResponse.Content.Results.Any());
            }

            await utils.DisconnectIdentities(frodo, sam);
        }

        [Test]
        public async Task SystemDefault_AnonymousVisitorsCannotViewConnections()
        {
            var utils = new ConfigurationTestUtilities(_scaffold);
            var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

            var frodoInitResponse = await merryOwnerClient.Configuration.InitializeIdentity(new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });
            
            ClassicAssert.IsTrue(frodoInitResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(frodoInitResponse.Content);
    
            var pippinInitResponse = await pippinOwnerClient.Configuration.InitializeIdentity( new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });
            
            ClassicAssert.IsTrue(pippinInitResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(pippinInitResponse.Content);
            

            var (frodo, sam, _) = await utils.CreateConnectionRequest(TestIdentities.Merry, TestIdentities.Pippin);
            await utils.AcceptConnectionRequest(sender: frodo, recipient: sam);

            var client = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Merry.OdinId);
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);

                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, "");
                ClassicAssert.IsTrue(getConnectionsResponse.StatusCode == HttpStatusCode.Forbidden, "Should have failed to get connections with 403 status code.");
            }

            await utils.DisconnectIdentities(frodo, sam);
        }
    }
}