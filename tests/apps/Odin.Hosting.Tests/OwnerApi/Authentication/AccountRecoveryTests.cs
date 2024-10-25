using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Time;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Authentication
{
    public class AccountRecoveryTests
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
        public async Task CanGetAccountRecoveryKey()
        {
            var identity = TestIdentities.Frodo;
            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, initializeIdentity: false);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var response = await ownerClient.Security.GetAccountRecoveryKey();

            Assert.IsTrue(response.IsSuccessStatusCode);

            var decryptedRecoveryKey = response.Content;
            Assert.IsTrue(decryptedRecoveryKey.Created < UnixTimeUtc.Now());
            Assert.IsNotEmpty(decryptedRecoveryKey.Key);
            Assert.IsNotNull(decryptedRecoveryKey.Key);
            Assert.IsTrue(decryptedRecoveryKey.Key.Split(" ").Length == 12,"there should be 12 words");

            //TODO: additional checks on the key
            // RecoveryKeyGenerator.Characters
        }

        [Test]
        public async Task CanResetPasswordUsingAccountRecoveryKey()
        {
            var identity = TestIdentities.TomBombadil;
            const string password = "8833CC039d!!~!";
            const string newPassword = "672c~!!9402044";

            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, password);

            //Ensure we can login using the first password
            var firstLoginResponse = await this.Login(identity.OdinId, password);
            Assert.IsTrue(firstLoginResponse.IsSuccessStatusCode);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var response = await ownerClient.Security.GetAccountRecoveryKey();
            Assert.IsTrue(response.IsSuccessStatusCode);

            var decryptedRecoveryKey = response.Content;
            Assert.IsTrue(decryptedRecoveryKey.Created < UnixTimeUtc.Now());

            var key = decryptedRecoveryKey.Key;
            
            //encrypt using RSA
            // _publicPrivateKeyService.EncryptPayload(RsaKeyType.OfflineKey, payload)
            
            var resetPasswordResponse = await ownerClient.Security.ResetPasswordUsingRecoveryKey(key, newPassword);
            Assert.IsTrue(resetPasswordResponse.IsSuccessStatusCode, $"failed resetting password to newPassword with key [{key}]");
            
            //login with the password
            var secondLogin = await this.Login(identity.OdinId, newPassword);
            Assert.IsTrue(secondLogin.IsSuccessStatusCode);

            // Additional tests
            // Test that I can access data in drives as owner; this shows the master key is the same
            // Test can i send a file over transit as owner; this shows the master key is still good for the Icr Encryption key
        }

        [Test]
        public async Task FailToResetPasswordUsingInvalidAccountRecoveryKey()
        {
            var identity = TestIdentities.Pippin;
            const string password = "8833CC039d!!~!";
            const string newPassword = "672c~!!9402044";

            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, password);

            //Ensure we can login using the first password
            var firstLogin = await this.Login(identity.OdinId, password);
            Assert.IsTrue(firstLogin.IsSuccessStatusCode);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var response = await ownerClient.Security.GetAccountRecoveryKey();
            Assert.IsTrue(response.IsSuccessStatusCode);

            var invalidRecoveryKey = Guid.NewGuid().ToString("N");
            var resetPasswordResponse = await ownerClient.Security.ResetPasswordUsingRecoveryKey(invalidRecoveryKey, newPassword);
            Assert.IsFalse(resetPasswordResponse.IsSuccessStatusCode,
                $"shoudl have failed resetting password to newPassword with an invalid recovery key [{invalidRecoveryKey}]");

            // Fail to login with new password
            var secondLogin = await this.Login(identity.OdinId, newPassword);
            Assert.IsTrue(secondLogin.StatusCode == HttpStatusCode.Forbidden, "Should have failed to login with the new password");

            // Succeed in logging in with old password
            var thirdLogin = await this.Login(identity.OdinId, password);
            Assert.IsTrue(thirdLogin.IsSuccessStatusCode, "Should have been able to login with old password");
        }

        private async Task<ApiResponse<OwnerAuthenticationResult>> Login(string identity, string password)
        {
            using HttpClient authClient = new()
            {
                BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}")
            };

            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            var nonceResponse = await svc.GenerateAuthenticationNonce();
            Assert.IsTrue(nonceResponse.IsSuccessStatusCode, "server failed when getting nonce");
            var clientNonce = nonceResponse.Content;

            var nonce = new NonceData(clientNonce!.SaltPassword64, clientNonce.SaltKek64, clientNonce.PublicPem, clientNonce.CRC)
            {
                Nonce64 = clientNonce.Nonce64
            };

            var reply = PasswordDataManager.CalculatePasswordReply(password, nonce);
            var response = await svc.Authenticate(reply);
            return response;
        }
    }
}