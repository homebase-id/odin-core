using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Configuration;

namespace Odin.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class AuthenticatedDefaultsTests
    {
        private WebScaffold _scaffold;

        [SetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: false);
        }

        [TearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }


        [Test]
        [Ignore("cannot automatically test until we have a login process for youauth")]
        public void CanAllowAuthenticatedVisitorsToViewConnections()
        {
            Assert.Inconclusive("TODO");
        }
        
        [Test]
        [Ignore("cannot automatically test until we have a login process for youauth")]
        public void CanBlockAuthenticatedVisitorsFromViewingConnections()
        {
            Assert.Inconclusive("TODO");
        }


        [Test]
        public async Task SystemDefault_TenantSettings_AuthenticatedIdentitiesCanReactOnAnonymousDrives_IsTrue()
        {
            var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);

            await merryOwnerClient.Configuration.InitializeIdentity(new InitialSetupRequest());

            var getSettingsResponse  = await merryOwnerClient.Configuration.GetTenantSettings();
            Assert.IsTrue(getSettingsResponse.IsSuccessStatusCode);
            Assert.IsTrue(getSettingsResponse.Content.AuthenticatedIdentitiesCanReactOnAnonymousDrives);
        }
        
        
        [Test]
        public async Task SystemDefault_TenantSettings_AuthenticatedIdentitiesCan_NOT_CommentOnAnonymousDrives_IsTrue()
        {
            var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);

            await merryOwnerClient.Configuration.InitializeIdentity(new InitialSetupRequest());

            var getSettingsResponse  = await merryOwnerClient.Configuration.GetTenantSettings();
            Assert.IsTrue(getSettingsResponse.IsSuccessStatusCode);
            Assert.IsFalse(getSettingsResponse.Content.AuthenticatedIdentitiesCanCommentOnAnonymousDrives);
        }
    }
}