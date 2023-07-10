using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Odin.Hosting.Tests.OwnerApi.Authentication
{
    public class AccountRecoveryTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        [Ignore("Setup additional digital identities dedicated to testing these")]
        public async Task CanGetAccountRecoveryKey()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            var key = await ownerClient.Security.GetAccountRecoveryKey();

            Assert.IsNotNull(key.Content);
            //TODO: additional checks on the key
        }

        [Test]
        [Ignore("Setup additional digital identities dedicated to testing these")]
        public async Task FailToGetAccountRecoveryKeyOutsideOfTimeWindow()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            var response = await ownerClient.Security.GetAccountRecoveryKey();
            Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);
        }

        [Test]
        [Ignore("Setup additional digital identities dedicated to testing these")]
        public async Task CanResetPasswordUsingAccountRecoveryKey()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            var response = await ownerClient.Security.GetAccountRecoveryKey();

            string recoveryKey = response.Content;
            const string newPassword = "##99coco!";

            await ownerClient.Security.ResetPassword(recoveryKey, newPassword);

            //login with new password
        }

        [Test]
        [Ignore("Setup additional digital identities dedicated to testing these")]
        public async Task FailToResetPasswordUsingInvalidAccountRecoveryKey()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            var response = await ownerClient.Security.GetAccountRecoveryKey();

            string recoveryKey = response.Content;
            const string newPassword = "##99coco!";

            await ownerClient.Security.ResetPassword(recoveryKey, newPassword);

            
            //validate can login w/ old password
        }
    }
}