using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Hosting.Tests.OwnerApi.Circle;

namespace Youverse.Hosting.Tests.OwnerApi.Configuration.SystemInit
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
        public async Task SystemDefault_ConnectedContactsCannotViewConnections()
        {
        }

        [Test]
        [Ignore("cannot automatically test until we have a login process for youauth")]
        public async Task CanAllowConnectedContactsToViewConnections()
        {
        }

        [Test]
        [Ignore("cannot automatically test until we have a login process for youauth")]
        public async Task CanBlockConnectedContactsFromViewingConnectionsUnlessInCircle()
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
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ss))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ss);

                var getSystemCircleResponse = await svc.GetCircleDefinition(CircleConstants.SystemCircleId);
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
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ss))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ss);

                var getSystemCircleResponse = await svc.GetCircleDefinition(CircleConstants.SystemCircleId);
                Assert.IsTrue(getSystemCircleResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getSystemCircleResponse.Content);
                var systemCircle = getSystemCircleResponse.Content;
                Assert.IsFalse(systemCircle.Permissions.Keys.Contains(PermissionKeys.ReadConnections));
            }
        }
    }
}