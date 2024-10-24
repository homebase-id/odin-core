using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Time;

namespace Odin.Hosting.Tests._Universal.Owner.AccountManagement
{
    public class DeleteAccountTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(false, false);
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
        public async Task CanMarkAccountForDeletion()
        {
            var identity = TestIdentities.Frodo;
            const string password = "8833CC039d!!~!";
            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, password);

            var ownerClient = _scaffold.CreateOwnerApiClientRedux(identity);

            var deleteAccountResponse = await ownerClient.AccountManagement.DeleteAccount(password);
            Assert.IsTrue(deleteAccountResponse.IsSuccessStatusCode);

            var getStatusResponse = await ownerClient.AccountManagement.GetAccountStatus();
            Assert.IsTrue(getStatusResponse.IsSuccessStatusCode);
            Assert.IsNotNull(getStatusResponse.Content.PlannedDeletionDate);
            Assert.IsTrue(getStatusResponse.Content.PlannedDeletionDate > UnixTimeUtc.Now());
        }

        [Test]
        public async Task CanUnmarkAccountForDeletion()
        {
            var identity = TestIdentities.TomBombadil;
            const string password = "8833CC039d!!~!";
            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, password);

            var ownerClient = _scaffold.CreateOwnerApiClientRedux(identity);

            //setup for delete
            var deleteAccountResponse = await ownerClient.AccountManagement.DeleteAccount(password);
            Assert.IsTrue(deleteAccountResponse.IsSuccessStatusCode);

            // make sure we're set to delete
            var getStatusResponse = await ownerClient.AccountManagement.GetAccountStatus();
            Assert.IsTrue(getStatusResponse.IsSuccessStatusCode);
            Assert.IsNotNull(getStatusResponse.Content.PlannedDeletionDate);
            Assert.IsTrue(getStatusResponse.Content.PlannedDeletionDate > UnixTimeUtc.Now());

            var unmarkAccountResponse = await ownerClient.AccountManagement.UndeleteAccount(password);
            Assert.IsTrue(unmarkAccountResponse.IsSuccessStatusCode);

            var getStatusResponse2 = await ownerClient.AccountManagement.GetAccountStatus();
            Assert.IsTrue(getStatusResponse2.IsSuccessStatusCode);
            Assert.IsNull(getStatusResponse2.Content.PlannedDeletionDate);
        }

    }
}