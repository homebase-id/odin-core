using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;
using Youverse.Core.Cryptography;

namespace DotYou.Kernel.CryptographyTests
{
    public class TestTokenManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        [Test]
        public void TokenBasePass()
        {
            // Not really a test. Partially used this to hand-step through and manually
            // validate the encryption keys match up from creation to decryption.

            // Pre-requisites
            var loginKek = new SecureKey(YFByteArray.GetRndByteArray(16)); // Simulate pre-existing

            // Create a new application and link the first client to it

            // First create a new app. The new app will contain a new pair of
            // (app-kek,app-dek). Both are encrypted with the loginKek and
            // can later be retrieved with the loginKek.
            //
            var appToken = AppRegistrationManager.CreateApplication(loginKek);

            // Now create a mapping from a client device/app to the application token above

            var applicationDek = AppRegistrationManager.GetApplicationDekWithLogin(appToken, loginKek);

            Assert.Pass();
        }


        [Test]
        public void TokenBase2Pass()
        {
            // Pre-requisites
            var loginKek = new SecureKey(YFByteArray.GetRndByteArray(16)); // Pre-existing

            // Create a new application and link the first client to it

            // First create a new app. The new app will contain a new pair of
            // (app-kek,app-dek). Both are encrypted with the loginKek and
            // can later be retrieved with the loginKek.
            //
            var appToken = AppRegistrationManager.CreateApplication(loginKek);

            // Now create a mapping from a client device/app to the application token above

            // First get the application DEK
            var appDekViaLogin = AppRegistrationManager.GetApplicationDekWithLogin(appToken, loginKek);

            // Now create the mapping
            // TODO: xxx the id needs to be removed
            var (halfKey, clientToken) = AppClientTokenManager.CreateClientToken(YFByteArray.GetRndByteArray(16), appDekViaLogin.GetKey());

            // The two cookies / keys to give to the client are:
            //   clientToken.TokenId ["Token"]
            //   cookie2             ["Half"]

            var appDekViaCookies = AppClientTokenManager.GetApplicationDek(clientToken, halfKey);

            if (YFByteArray.EquiByteArrayCompare(appDekViaLogin.GetKey(), appDekViaCookies.GetKey()) == false)
            {
                Assert.Fail();
                return;
            }

        }

    }
}
