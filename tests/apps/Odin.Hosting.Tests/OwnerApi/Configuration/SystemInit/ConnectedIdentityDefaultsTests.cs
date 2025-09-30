using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration;
using Odin.Services.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.Membership.Circles;
using System.Collections.Generic;

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
            _scaffold.RunBeforeAnyTests(initializeIdentity: false, testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Merry });
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
            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

            var frodoInitResponse = await frodoOwnerClient.Configuration.InitializeIdentity(new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });

            ClassicAssert.IsTrue(frodoInitResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(frodoInitResponse.Content);


            await frodoOwnerClient.Configuration.UpdateTenantSettingsFlag(TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections, bool.TrueString);

            var getSystemCircleResponse1 = await frodoOwnerClient.Membership.GetCircleDefinition(SystemCircleConstants.ConfirmedConnectionsCircleId);
            ClassicAssert.IsTrue(getSystemCircleResponse1.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getSystemCircleResponse1.Content);

            var systemCircle1 = getSystemCircleResponse1.Content;
            ClassicAssert.IsTrue(systemCircle1.Permissions.Keys.Contains(PermissionKeys.ReadConnections));

            //
            // Disable ability to read connections
            //
            await frodoOwnerClient.Configuration.UpdateTenantSettingsFlag(TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections, bool.FalseString);

            //
            // system circle should not have permissions
            //
            var getSystemCircleResponse2 = await frodoOwnerClient.Membership.GetCircleDefinition(SystemCircleConstants.ConfirmedConnectionsCircleId);
            ClassicAssert.IsTrue(getSystemCircleResponse2.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getSystemCircleResponse2.Content);
            var systemCircle = getSystemCircleResponse2.Content;
            ClassicAssert.IsFalse(systemCircle.Permissions.Keys.Contains(PermissionKeys.ReadConnections));
        }
        
        [Test]
        public async Task SystemDefault_TenantSettings_ConnectedIdentitiesCanReactOnAnonymousDrives_IsTrue()
        {
            var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);

            await merryOwnerClient.Configuration.InitializeIdentity(new InitialSetupRequest());

            var getSettingsResponse  = await merryOwnerClient.Configuration.GetTenantSettings();
            ClassicAssert.IsTrue(getSettingsResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getSettingsResponse.Content.ConnectedIdentitiesCanReactOnAnonymousDrives);
        }
        
        [Test]
        public async Task SystemDefault_TenantSettings_AutoAcceptIntroductions_IsTrue()
        {
            var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);

            await merryOwnerClient.Configuration.InitializeIdentity(new InitialSetupRequest());

            var getSettingsResponse  = await merryOwnerClient.Configuration.GetTenantSettings();
            ClassicAssert.IsTrue(getSettingsResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getSettingsResponse.Content.ConnectedIdentitiesCanReactOnAnonymousDrives);
        }
        
        
        [Test]
        public async Task SystemDefault_TenantSettings_ConnectedIdentitiesCanCommentOnAnonymousDrives_IsTrue()
        {
            var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);

            await merryOwnerClient.Configuration.InitializeIdentity(new InitialSetupRequest());

            var getSettingsResponse  = await merryOwnerClient.Configuration.GetTenantSettings();
            ClassicAssert.IsTrue(getSettingsResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getSettingsResponse.Content.ConnectedIdentitiesCanCommentOnAnonymousDrives);
        }
    }
}