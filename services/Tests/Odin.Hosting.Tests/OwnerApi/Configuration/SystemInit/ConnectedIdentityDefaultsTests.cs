using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.Membership.Circles;

namespace Odin.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class ConnectedIdentityDefaultsTests
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
        [Ignore("cannot automatically test until we have a login process for youauth")]
        public void SystemDefault_ConnectedContactsCannotViewConnections()
        {
        }

        [Test]
        [Ignore("cannot automatically test until we have a login process for youauth")]
        public void CanAllowConnectedContactsToViewConnections()
        {
        }

        [Test]
        [Ignore("cannot automatically test until we have a login process for youauth")]
        public void CanBlockConnectedContactsFromViewingConnectionsUnlessInCircle()
        {
            Assert.Inconclusive("TODO");
        }

        [Test]
        public async Task SystemCircleUpdatedWhenConnectedFlagChanges()
        {
            var identity = TestIdentities.Frodo;
            var utils = new ConfigurationTestUtilities(_scaffold);

            await _scaffold.OldOwnerApi.InitializeIdentity(identity, new InitialSetupRequest());
            
            await utils.UpdateSystemConfigFlag(identity, TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections.ToString(), bool.TrueString);
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ss);
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ss);

                var getSystemCircleResponse = await svc.GetCircleDefinition(CircleConstants.ConnectedIdentitiesSystemCircleId);
                Assert.IsTrue(getSystemCircleResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getSystemCircleResponse.Content);
                var systemCircle = getSystemCircleResponse.Content;
                Assert.IsTrue(systemCircle.Permissions.Keys.Contains(PermissionKeys.ReadConnections));
            }
            
            //
            // disable ability to read connections
            //
            await utils.UpdateSystemConfigFlag(identity, TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections.ToString(), bool.FalseString);
          
            // system circle should not have permissions
            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out ss);
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ss);

                var getSystemCircleResponse = await svc.GetCircleDefinition(CircleConstants.ConnectedIdentitiesSystemCircleId);
                Assert.IsTrue(getSystemCircleResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getSystemCircleResponse.Content);
                var systemCircle = getSystemCircleResponse.Content;
                Assert.IsFalse(systemCircle.Permissions.Keys.Contains(PermissionKeys.ReadConnections));
            }
        }
    }
}