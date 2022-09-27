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

            var identity = TestIdentities.Samwise;
            await _scaffold.OwnerApi.InitializeIdentity(identity, new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });
            
            var (frodo, sam, _) = await utils.CreateConnectionRequestFrodoToSam();
            await utils.AcceptConnectionRequest(sender: frodo, recipient: sam);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);
                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, 100);
                Assert.IsTrue(getConnectionsResponse.StatusCode == HttpStatusCode.Forbidden, "Should have failed to get connections with 403 status code.");
            }

            await UpdateSystemConfigFlag(identity, TenantConfigFlagNames.AnonymousVisitorsCanViewConnections.ToString(), bool.TrueString);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);

                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, 100);
                Assert.IsTrue(getConnectionsResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getConnectionsResponse.Content);
                Assert.IsTrue(getConnectionsResponse.Content.Results.Any());
            }

            await utils.DisconnectIdentities(frodo, sam);
        }

        private async Task UpdateSystemConfigFlag(TestIdentity identity, string flag, string value)
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);
                var updateFlagResponse = await svc.UpdateSystemConfigFlag(new UpdateFlagRequest()
                {
                    FlagName = flag,
                    Value = value
                });

                Assert.IsTrue(updateFlagResponse.IsSuccessStatusCode, "system should return empty settings when first initialized");
            }
        }
        
        [Test]
        public async Task SystemDefault_AnonymousAnonymousVisitorsCannotViewConnections()
        {
            var utils = new ConfigurationTestUtilities(_scaffold);
            await _scaffold.OwnerApi.InitializeIdentity(TestIdentities.Frodo, new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });

            var (frodo, sam, _) = await utils.CreateConnectionRequestFrodoToSam();
            await utils.AcceptConnectionRequest(sender: frodo, recipient: sam);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Frodo.DotYouId))
            {
                var youAuthCircleSvc = RestService.For<ICircleNetworkYouAuthClient>(client);

                var getConnectionsResponse = await youAuthCircleSvc.GetConnectedProfiles(1, 100);
                Assert.IsTrue(getConnectionsResponse.StatusCode == HttpStatusCode.Forbidden, "Should have failed to get connections with 403 status code.");
            }

            await utils.DisconnectIdentities(frodo, sam);
        }

    }
}