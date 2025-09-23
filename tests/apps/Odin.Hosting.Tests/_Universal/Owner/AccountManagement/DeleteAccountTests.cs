using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography;
using Odin.Core.Time;

namespace Odin.Hosting.Tests._Universal.Owner.AccountManagement
{
    public class DeleteAccountTests
    {
        private WebScaffold _scaffold;
        private OdinCryptoConfig _cryptoConfig = null!;

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
            _cryptoConfig = _scaffold.GetCryptoConfig();
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

            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, _cryptoConfig, password);

            var ownerClient = _scaffold.CreateOwnerApiClientRedux(identity);

            var deleteAccountResponse = await ownerClient.AccountManagement.DeleteAccount(password, _cryptoConfig);
            ClassicAssert.IsTrue(deleteAccountResponse.IsSuccessStatusCode);

            var getStatusResponse = await ownerClient.AccountManagement.GetAccountStatus();
            ClassicAssert.IsTrue(getStatusResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getStatusResponse.Content.PlannedDeletionDate);
            ClassicAssert.IsTrue(getStatusResponse.Content.PlannedDeletionDate > UnixTimeUtc.Now());
        }

        [Test]
        public async Task CanUnmarkAccountForDeletion()
        {
            var identity = TestIdentities.TomBombadil;
            const string password = "8833CC039d!!~!";
            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, _cryptoConfig, password);

            var ownerClient = _scaffold.CreateOwnerApiClientRedux(identity);

            //setup for delete
            var deleteAccountResponse = await ownerClient.AccountManagement.DeleteAccount(password, _cryptoConfig);
            ClassicAssert.IsTrue(deleteAccountResponse.IsSuccessStatusCode);

            // make sure we're set to delete
            var getStatusResponse = await ownerClient.AccountManagement.GetAccountStatus();
            ClassicAssert.IsTrue(getStatusResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getStatusResponse.Content.PlannedDeletionDate);
            ClassicAssert.IsTrue(getStatusResponse.Content.PlannedDeletionDate > UnixTimeUtc.Now());

            var unmarkAccountResponse = await ownerClient.AccountManagement.UndeleteAccount(password, _cryptoConfig);
            ClassicAssert.IsTrue(unmarkAccountResponse.IsSuccessStatusCode);

            var getStatusResponse2 = await ownerClient.AccountManagement.GetAccountStatus();
            ClassicAssert.IsTrue(getStatusResponse2.IsSuccessStatusCode);
            ClassicAssert.IsNull(getStatusResponse2.Content.PlannedDeletionDate);
        }

    }
}