using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Hosting.Tests.YouAuthApi.Circle;
using Refit;

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
            _scaffold.RunBeforeAnyTests(initializeIdentity: false);
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
            
            Assert.IsTrue(frodoInitResponse.IsSuccessStatusCode);
            Assert.IsTrue(frodoInitResponse.Content);
    
            var samInitResponse = await samOwnerClient.Configuration.InitializeIdentity( new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });
            
            Assert.IsTrue(samInitResponse.IsSuccessStatusCode);
            Assert.IsTrue(samInitResponse.Content);

            var (frodo, sam, _) = await utils.CreateConnectionRequestFrodoToSam();
            await utils.AcceptConnectionRequest(sender: frodo, recipient: sam);

            var client = _scaffold.CreateAnonymousApiHttpClient(samOwnerClient.Identity.OdinId);
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);
                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, 0);
                Assert.IsTrue(getConnectionsResponse.StatusCode == HttpStatusCode.Forbidden, "Should have failed to get connections with 403 status code.");
            }

            await samOwnerClient.Configuration.UpdateTenantSettingsFlag(TenantConfigFlagNames.AnonymousVisitorsCanViewConnections, bool.TrueString);

            client = _scaffold.CreateAnonymousApiHttpClient(samOwnerClient.Identity.OdinId);
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);

                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, 0);
                Assert.IsTrue(getConnectionsResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getConnectionsResponse.Content);
                Assert.IsTrue(getConnectionsResponse.Content.Results.Any());
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
            
            Assert.IsTrue(frodoInitResponse.IsSuccessStatusCode);
            Assert.IsTrue(frodoInitResponse.Content);
    
            var pippinInitResponse = await pippinOwnerClient.Configuration.InitializeIdentity( new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });
            
            Assert.IsTrue(pippinInitResponse.IsSuccessStatusCode);
            Assert.IsTrue(pippinInitResponse.Content);
            

            var (frodo, sam, _) = await utils.CreateConnectionRequest(TestIdentities.Merry, TestIdentities.Pippin);
            await utils.AcceptConnectionRequest(sender: frodo, recipient: sam);

            var client = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Merry.OdinId);
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);

                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, 100);
                Assert.IsTrue(getConnectionsResponse.StatusCode == HttpStatusCode.Forbidden, "Should have failed to get connections with 403 status code.");
            }

            await utils.DisconnectIdentities(frodo, sam);
        }
    }
}