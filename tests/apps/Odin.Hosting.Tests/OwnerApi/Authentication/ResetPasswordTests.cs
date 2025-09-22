using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Services.Authentication.Owner;
using Odin.Services.Registry.Registration;
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
            await _scaffold.OldOwnerApi.SetupOwnerAccount(TestIdentities.TomBombadil.OdinId, true);

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
    }
}