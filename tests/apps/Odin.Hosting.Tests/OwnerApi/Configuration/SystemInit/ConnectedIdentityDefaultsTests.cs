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
        public async Task TheConnectionsFlagNoLongerWritesAKeyOntoTheSystemCircle()
        {
            // This used to assert the opposite: enabling the flag put ReadConnections on the Confirmed
            // circle. That was the system treating *reviewed* as the operative tier without having a name
            // for it -- and it handed the key to every member of that circle, reviewed or not.
            //
            // The key is granted from the reviewed tier now. A copy on the circle is not redundant, it is
            // a second source that defeats the gate, so the flag strips it instead of writing it. Whether
            // the setting actually works is covered by ReviewedTierDistributionTests.
            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

            var frodoInitResponse = await frodoOwnerClient.Configuration.InitializeIdentity(new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            });

            ClassicAssert.IsTrue(frodoInitResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(frodoInitResponse.Content);

            await frodoOwnerClient.Configuration.UpdateTenantSettingsFlag(
                TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections, bool.TrueString);

            var enabled = await frodoOwnerClient.Membership.GetCircleDefinition(
                SystemCircleConstants.ConfirmedConnectionsCircleId);

            ClassicAssert.IsTrue(enabled.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(enabled.Content);
            ClassicAssert.IsFalse(enabled.Content.Permissions.Keys.Contains(PermissionKeys.ReadConnections),
                "enabling the setting must not put the key on the circle -- it comes from the tier");
            ClassicAssert.IsFalse(enabled.Content.Permissions.Keys.Contains(PermissionKeys.ReadWhoIFollow));

            await frodoOwnerClient.Configuration.UpdateTenantSettingsFlag(
                TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections, bool.FalseString);

            var disabled = await frodoOwnerClient.Membership.GetCircleDefinition(
                SystemCircleConstants.ConfirmedConnectionsCircleId);

            ClassicAssert.IsTrue(disabled.IsSuccessStatusCode);
            ClassicAssert.IsFalse(disabled.Content.Permissions.Keys.Contains(PermissionKeys.ReadConnections));

            // ...and the setting itself still round-trips.
            var settings = await frodoOwnerClient.Configuration.GetTenantSettings();
            ClassicAssert.IsTrue(settings.IsSuccessStatusCode);
            ClassicAssert.IsFalse(settings.Content.AllConnectedIdentitiesCanViewConnections);
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