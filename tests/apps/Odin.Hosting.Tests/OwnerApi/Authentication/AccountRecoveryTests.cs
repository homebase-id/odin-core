using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Time;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Authentication
{
    public class AccountRecoverySectionTests
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

            // let us say the user already has their key from before
            await ownerClient.Security.ConfirmStoredRecoveryKey();

            // since we just set up the account - first request the recovery key
            var requestRecoveryKeyResponse = await ownerClient.Security.RequestRecoveryKey();
            
            Assert.That(requestRecoveryKeyResponse.IsSuccessful, Is.True);
            Assert.That(requestRecoveryKeyResponse.Content, Is.Not.Null);
            var nextDate = requestRecoveryKeyResponse.Content.NextViewableDate;
            //
            // Thread.Sleep(11*1000); // sleep for 10 seconds to ensure the next viewable date is different;

            // time keeps ticking
            var nextViewableUtc = DateTimeOffset.FromUnixTimeMilliseconds(nextDate.milliseconds + 100);
            var now = DateTimeOffset.UtcNow;
            if (nextViewableUtc > now)
            {
                var delay = nextViewableUtc - now;
                await Task.Delay(delay);
            }

            var response = await ownerClient.Security.GetAccountRecoveryKey();
            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"status code was {response.StatusCode} but should have been OK");

            var decryptedRecoveryKey = response.Content;
            ClassicAssert.IsTrue(decryptedRecoveryKey.Created < UnixTimeUtc.Now());
            ClassicAssert.IsNotEmpty(decryptedRecoveryKey.Key);
            ClassicAssert.IsNotNull(decryptedRecoveryKey.Key);
            ClassicAssert.IsTrue(decryptedRecoveryKey.Key.Split(" ").Length == 12, "there should be 12 words");

            //TODO: additional checks on the key
            // RecoveryKeyGenerator.Characters
        }

        [Test]
        public async Task CanToGetAccountRecoveryKeyWhenViewedAfterTimeWindow()
        {
            var identity = TestIdentities.Samwise;
            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, initializeIdentity: false);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            // let us say the user already has their key from before
            await ownerClient.Security.ConfirmStoredRecoveryKey();
            
            var requestRecoveryKeyResponse = await ownerClient.Security.RequestRecoveryKey();
            Assert.That(requestRecoveryKeyResponse.IsSuccessful, Is.True);
            var result = requestRecoveryKeyResponse.Content;
            Assert.That(result, Is.Not.Null);

            // time keeps ticking
            var nextViewableUtc = DateTimeOffset.FromUnixTimeMilliseconds(result.NextViewableDate.milliseconds + 100);
            var now = DateTimeOffset.UtcNow;
            if (nextViewableUtc > now)
            {
                var delay = nextViewableUtc - now;
                await Task.Delay(delay);
            }

            var response = await ownerClient.Security.GetAccountRecoveryKey();
            ClassicAssert.IsTrue(response.IsSuccessStatusCode);

            var decryptedRecoveryKey = response.Content;
            ClassicAssert.IsTrue(decryptedRecoveryKey.Created < UnixTimeUtc.Now());
            ClassicAssert.IsNotEmpty(decryptedRecoveryKey.Key);
            ClassicAssert.IsNotNull(decryptedRecoveryKey.Key);
            ClassicAssert.IsTrue(decryptedRecoveryKey.Key.Split(" ").Length == 12, "there should be 12 words");

            // this should fail because we've cleared
            var response2 = await ownerClient.Security.GetAccountRecoveryKey();
            Assert.That(string.IsNullOrEmpty(response2.Content.Key), Is.True);
        }

        [Test]
        public async Task FailToGetAccountRecoveryKeyWhenViewedBeforeTimeWindow()
        {
            var identity = TestIdentities.Pippin;
            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, initializeIdentity: false);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            // let us say the user already has their key from before
            await ownerClient.Security.ConfirmStoredRecoveryKey();

            // make the first request since we just set up the account
            var requestRecoveryKeyResponse = await ownerClient.Security.RequestRecoveryKey();
            Assert.That(requestRecoveryKeyResponse.IsSuccessful, Is.True);
            var result = requestRecoveryKeyResponse.Content;
            Assert.That(result, Is.Not.Null);

            // time keeps ticking; wait 1 second less than is required
            var nextViewableUtc = DateTimeOffset.FromUnixTimeMilliseconds(result.NextViewableDate.milliseconds - 1000);
            var now = DateTimeOffset.UtcNow;
            if (nextViewableUtc > now)
            {
                var delay = nextViewableUtc - now;
                await Task.Delay(delay);
            }

            var response = await ownerClient.Security.GetAccountRecoveryKey();
            ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.OK);
            ClassicAssert.IsTrue(!string.IsNullOrEmpty(response.Content.Key));

            // now make a second request
            var requestRecoveryKeyResponse2 = await ownerClient.Security.RequestRecoveryKey();
            Assert.That(requestRecoveryKeyResponse2.IsSuccessful, Is.True);
            var result2 = requestRecoveryKeyResponse2.Content;
            Assert.That(result2, Is.Not.Null);

            // time keeps ticking
            // var nextViewableUtc2 = DateTimeOffset.FromUnixTimeMilliseconds(result2.NextViewableDate.milliseconds + 100);
            // var now2 = DateTimeOffset.UtcNow;
            // if (nextViewableUtc2 > now2)
            // {
            //     var delay = nextViewableUtc2 - now2;
            //     await Task.Delay(delay);
            // }
            //
            var response2 = await ownerClient.Security.GetAccountRecoveryKey();
            ClassicAssert.IsTrue(response2.Content.Key == null);
        }

        [Test]
        public async Task CanResetPasswordUsingAccountRecoveryKey()
        {
            var identity = TestIdentities.TomBombadil;
            const string password = "8833CC039d!!~!";
            const string newPassword = "672c~!!9402044";

            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, password);

            var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

            //Ensure we can login using the first password
            var firstLoginResponse = await this.Login(identity.OdinId, password, clientEccFullKey);
            ClassicAssert.IsTrue(firstLoginResponse.IsSuccessStatusCode);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            // let us say the user already has their key from before
            await ownerClient.Security.ConfirmStoredRecoveryKey();
            
            var requestRecoveryKeyResponse = await ownerClient.Security.RequestRecoveryKey();
            Assert.That(requestRecoveryKeyResponse.IsSuccessful, Is.True);
            var result = requestRecoveryKeyResponse.Content;
            Assert.That(result, Is.Not.Null);

            // time keeps ticking
            var nextViewableUtc = DateTimeOffset.FromUnixTimeMilliseconds(result.NextViewableDate.milliseconds + 100);
            var now = DateTimeOffset.UtcNow;
            if (nextViewableUtc > now)
            {
                var delay = nextViewableUtc - now;
                await Task.Delay(delay);
            }

            var response = await ownerClient.Security.GetAccountRecoveryKey();
            ClassicAssert.IsTrue(response.IsSuccessStatusCode);

            var decryptedRecoveryKey = response.Content;
            ClassicAssert.IsTrue(decryptedRecoveryKey.Created < UnixTimeUtc.Now());

            var key = decryptedRecoveryKey.Key;

            var resetPasswordResponse = await ownerClient.Security.ResetPasswordUsingRecoveryKey(key, newPassword);
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
            var identity = TestIdentities.Merry;
            const string password = "8833CC039d!!~!";
            const string newPassword = "672c~!!9402044";

            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, password);
            var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

            //Ensure we can login using the first password
            var firstLogin = await this.Login(identity.OdinId, password, clientEccFullKey);
            ClassicAssert.IsTrue(firstLogin.IsSuccessStatusCode);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var response = await ownerClient.Security.GetAccountRecoveryKey();
            ClassicAssert.IsTrue(response.IsSuccessStatusCode);

            var invalidRecoveryKey = Guid.NewGuid().ToString("N");
            var resetPasswordResponse = await ownerClient.Security.ResetPasswordUsingRecoveryKey(invalidRecoveryKey, newPassword);
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

            var nonce = new NonceData(clientNonce!.SaltPassword64, clientNonce.SaltKek64, clientNonce.PublicJwk, clientNonce.CRC)
            {
                Nonce64 = clientNonce.Nonce64
            };

            var reply = PasswordDataManager.CalculatePasswordReply(password, nonce, clientEccFullKey);
            var response = await svc.Authenticate(reply);
            return response;
        }
    }
}