using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Registry.Registration;
using Odin.Services.Tenant.Container;
using Odin.Core.Time;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Refit;
using Odin.Core.Identity;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Hosting.Tests.OwnerApi.Authentication
{
    public class ResetPasswordTests
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
        public async Task CanResetPasswordUsingCurrentPassword()
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

            var resetPasswordResponse = await ownerClient.Security.ResetPassword(password, newPassword);
            ClassicAssert.IsTrue(resetPasswordResponse.IsSuccessStatusCode, $"failed resetting password to newPassword with key");

            //login with the password
            var secondLogin = await this.Login(identity.OdinId, newPassword, clientEccFullKey);
            ClassicAssert.IsTrue(secondLogin.IsSuccessStatusCode);
            
            //fail to login with the old password
            var thirdLogin = await this.Login(identity.OdinId, password, clientEccFullKey);
            ClassicAssert.IsFalse(thirdLogin.IsSuccessStatusCode, "Should have failed to login with old password");

            // Additional tests
            // Test that I can access data in drives as owner; this shows the master key is the same
            // Test can i send a file over transit as owner; this shows the master key is still good for the Icr Encryption key
        }

        // Regression: a stale-KEK cookie (valid Id but the registration's KEK no longer
        // unwraps the rewritten PasswordData.KekEncryptedMasterKey) used to pass
        // verifyToken because the endpoint only checked Id presence. That made the owner
        // login page redirect-bounce into /api/owner/v1/youauth/authorize forever, since
        // useValidateAuthorization on the client only forces logout when verifyToken
        // returns false. See youauth-redirect-loop.md.
        [Test]
        public async Task VerifyTokenReturnsFalseForStaleCookieAfterPasswordReset()
        {
            var identity = TestIdentities.Pippin;
            const string password = "8833CC039d!!~!";
            const string newPassword = "672c~!!9402044";

            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, password);

            // Keep our own cookie jar so the cookie survives the later password-reset call.
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using var browserClient = new HttpClient(handler)
            {
                BaseAddress = new Uri($"https://{identity.OdinId.DomainName}:{WebScaffold.HttpsPort}"),
            };

            var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            var reply = await _scaffold.OldOwnerApi.CalculateAuthenticationPasswordReply(
                browserClient, password, clientEccFullKey);
            var authSvc = RestService.For<IOwnerAuthenticationClient>(browserClient);
            var loginResponse = await authSvc.Authenticate(reply);
            ClassicAssert.IsTrue(loginResponse.IsSuccessStatusCode, "Initial login failed");

            // Fresh cookie: verifyToken must accept it.
            {
                using var resp = await browserClient.GetAsync("/api/owner/v1/authentication/verifyToken");
                ClassicAssert.IsTrue(resp.IsSuccessStatusCode);
                var body = await resp.Content.ReadAsStringAsync();
                ClassicAssert.AreEqual("true", body, "verifyToken should accept a fresh cookie");
            }

            // Rewrite PasswordData via a separate owner session. The registration that
            // browserClient is holding now has TokenEncryptedKek wrapping the old KEK
            // while PasswordData.KekEncryptedMasterKey is wrapped with the new KEK.
            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var resetResponse = await ownerClient.Security.ResetPassword(password, newPassword);
            ClassicAssert.IsTrue(resetResponse.IsSuccessStatusCode, "ResetPassword failed");

            // OdinContextCache caches the pre-reset context against the cookie's
            // composite key. In production it eventually expires (or the server
            // restarts) and the loop only manifests after that. Reset it explicitly
            // here so the test exercises the same post-expiry decrypt path.
            await ResetTenantContextCache(identity.OdinId);

            // Stale cookie: verifyToken must reject it.
            {
                using var resp = await browserClient.GetAsync("/api/owner/v1/authentication/verifyToken");
                ClassicAssert.IsTrue(resp.IsSuccessStatusCode,
                    $"verifyToken responded with {resp.StatusCode}");
                var body = await resp.Content.ReadAsStringAsync();
                ClassicAssert.AreEqual("false", body,
                    "verifyToken must reject a stale-KEK cookie; otherwise the login page redirect-loops");
            }
        }

        [Test]
        public async Task FailToResetPasswordUsingInvalidOldPassword()
        {
            var identity = TestIdentities.Merry;
            const string password = "8833CC039d!!~!";
            const string newPassword = "672c~!!9402044";
            const string invalidOldPassword = password + "382";

            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, password);
            var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

            //Ensure we can login using the first password
            var firstLoginResponse = await this.Login(identity.OdinId, password, clientEccFullKey);
            ClassicAssert.IsTrue(firstLoginResponse.IsSuccessStatusCode);

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            var resetPasswordResponse = await ownerClient.Security.ResetPassword(invalidOldPassword, newPassword);
            ClassicAssert.IsFalse(resetPasswordResponse.IsSuccessStatusCode, $"Should have failed to reset password using invalid old password");

            //Ensure we can still login using the first password
            var secondLogin = await this.Login(identity.OdinId, password, clientEccFullKey);
            ClassicAssert.IsTrue(secondLogin.IsSuccessStatusCode);
        }

        private async Task<ApiResponse<OwnerAuthenticationResult>> Login(OdinId identity, string password, EccFullKeyData clientEccFullKey)
        {
            var authClient = _scaffold.OldOwnerApi.CreateAnonymousClient(identity);

            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            var reply = await _scaffold.OldOwnerApi.CalculateAuthenticationPasswordReply(authClient, password, clientEccFullKey);
            var response = await svc.Authenticate(reply);
            return response;
        }

        private async Task ResetTenantContextCache(OdinId tenant)
        {
            var container = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
            var scope = container.GetTenantScope(tenant.DomainName);
            await scope.Resolve<OdinContextCache>().ResetAsync();
        }
    }
}