using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
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
        public async Task CanGetAccountRecoveryKey()
        {
            var identity = TestIdentities.Frodo;
            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, initializeIdentity: false, _cryptoConfig);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var response = await ownerClient.Security.GetAccountRecoveryKey();

            ClassicAssert.IsTrue(response.IsSuccessStatusCode);

            var decryptedRecoveryKey = response.Content;
            ClassicAssert.IsTrue(decryptedRecoveryKey.Created < UnixTimeUtc.Now());
            ClassicAssert.IsNotEmpty(decryptedRecoveryKey.Key);
            ClassicAssert.IsNotNull(decryptedRecoveryKey.Key);
            ClassicAssert.IsTrue(decryptedRecoveryKey.Key.Split(" ").Length == 12,"there should be 12 words");

            //TODO: additional checks on the key
            // RecoveryKeyGenerator.Characters
        }

        [Test]
        public async Task CanResetPasswordUsingAccountRecoveryKey()
        {
            var identity = TestIdentities.TomBombadil;
            const string password = "8833CC039d!!~!";
            const string newPassword = "672c~!!9402044";

            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, _cryptoConfig, password);

            var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

            //Ensure we can login using the first password
            var firstLoginResponse = await this.Login(identity.OdinId, password, clientEccFullKey);
            ClassicAssert.IsTrue(firstLoginResponse.IsSuccessStatusCode);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var response = await ownerClient.Security.GetAccountRecoveryKey();
            ClassicAssert.IsTrue(response.IsSuccessStatusCode);

            var decryptedRecoveryKey = response.Content;
            ClassicAssert.IsTrue(decryptedRecoveryKey.Created < UnixTimeUtc.Now());

            var key = decryptedRecoveryKey.Key;
            
            //encrypt using RSA
            // _publicPrivateKeyService.EncryptPayload(RsaKeyType.OfflineKey, payload)
            
            var resetPasswordResponse = await ownerClient.Security.ResetPasswordUsingRecoveryKey(key, newPassword, _cryptoConfig);
            ClassicAssert.IsTrue(resetPasswordResponse.IsSuccessStatusCode, $"failed resetting password to newPassword with key [{key}]");

            //login with the password
            var secondLogin = await this.Login(identity.OdinId, newPassword, clientEccFullKey);
            ClassicAssert.IsTrue(secondLogin.IsSuccessStatusCode);

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

            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, _cryptoConfig, password);
            var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

            //Ensure we can login using the first password
            var firstLogin = await this.Login(identity.OdinId, password, clientEccFullKey);
            ClassicAssert.IsTrue(firstLogin.IsSuccessStatusCode);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var response = await ownerClient.Security.GetAccountRecoveryKey();
            ClassicAssert.IsTrue(response.IsSuccessStatusCode);

            var invalidRecoveryKey = Guid.NewGuid().ToString("N");
            var resetPasswordResponse = await ownerClient.Security.ResetPasswordUsingRecoveryKey(invalidRecoveryKey, newPassword, _cryptoConfig);
            ClassicAssert.IsFalse(resetPasswordResponse.IsSuccessStatusCode,
                $"shoudl have failed resetting password to newPassword with an invalid recovery key [{invalidRecoveryKey}]");

            // Fail to login with new password
            var secondLogin = await this.Login(identity.OdinId, newPassword, clientEccFullKey);
            ClassicAssert.IsTrue(secondLogin.StatusCode == HttpStatusCode.Forbidden, "Should have failed to login with the new password");

            // Succeed in logging in with old password
            var thirdLogin = await this.Login(identity.OdinId, password, clientEccFullKey);
            ClassicAssert.IsTrue(thirdLogin.IsSuccessStatusCode, "Should have been able to login with old password");
        }

        private async Task<ApiResponse<OwnerAuthenticationResult>> Login(string identity, string password, EccFullKeyData clientEccFullKey)
        {
            using HttpClient authClient = new()
            {
                BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}")
            };

            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            var nonceResponse = await svc.GenerateAuthenticationNonce();
            ClassicAssert.IsTrue(nonceResponse.IsSuccessStatusCode, "server failed when getting nonce");
            var clientNonce = nonceResponse.Content;

            var nonce = new NonceData(clientNonce!.SaltPassword64, clientNonce.SaltKek64, clientNonce.PublicJwk, clientNonce.CRC, _cryptoConfig.HashSize)
            {
                Nonce64 = clientNonce.Nonce64
            };

            var passwordDataManager = new PasswordDataManager(_cryptoConfig);
            var reply = passwordDataManager.CalculatePasswordReply(password, nonce, clientEccFullKey);
            var response = await svc.Authenticate(reply);
            return response;
        }
    }
}