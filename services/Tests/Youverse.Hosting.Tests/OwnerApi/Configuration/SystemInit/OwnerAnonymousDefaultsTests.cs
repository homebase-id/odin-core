using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Configuration;
using Youverse.Hosting.Tests.YouAuthApi.Circle;

namespace Youverse.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class OwnerAnonymousDefaultsTests
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
        public async Task CanAllowAnonymousToViewConnections()
        {
            var utils = new ConfigurationTestUtilities(_scaffold);

            await _scaffold.OldOwnerApi.InitializeIdentity(TestIdentities.Frodo, new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });


            var identity = TestIdentities.Samwise;
            await _scaffold.OldOwnerApi.InitializeIdentity(identity, new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });


            var (frodo, sam, _) = await utils.CreateConnectionRequestFrodoToSam();
            await utils.AcceptConnectionRequest(sender: frodo, recipient: sam);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId))
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);
                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, 0);
                Assert.IsTrue(getConnectionsResponse.StatusCode == HttpStatusCode.Forbidden, "Should have failed to get connections with 403 status code.");
            }

            await utils.UpdateSystemConfigFlag(identity, TenantConfigFlagNames.AnonymousVisitorsCanViewConnections.ToString(), bool.TrueString);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId))
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
            await _scaffold.OldOwnerApi.InitializeIdentity(TestIdentities.Frodo, new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });
            
            await _scaffold.OldOwnerApi.InitializeIdentity(TestIdentities.Samwise, new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });

            var (frodo, sam, _) = await utils.CreateConnectionRequestFrodoToSam();
            await utils.AcceptConnectionRequest(sender: frodo, recipient: sam);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Frodo.OdinId))
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);

                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, 100);
                Assert.IsTrue(getConnectionsResponse.StatusCode == HttpStatusCode.Forbidden, "Should have failed to get connections with 403 status code.");
            }

            await utils.DisconnectIdentities(frodo, sam);
        }
    }
}