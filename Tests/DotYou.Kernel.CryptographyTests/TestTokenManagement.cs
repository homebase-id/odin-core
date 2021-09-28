using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DotYou.Kernel.Cryptography;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;

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
            var loginKek = YFByteArray.GetRndByteArray(16); // Simulate pre-existing

            // Create a new application and link the first client to it

            // First create a new app. The new app will contain a new pair of
            // (app-kek,app-dek). Both are encrypted with the loginKek and
            // can later be retrieved with the loginKek.
            //
            var appToken = AppKeyManager.CreateApplication("chat", loginKek);

            // Now create a mapping from a client device/app to the application token above

            var applicationDek = AppKeyManager.LoginGetApplicationDek(appToken, loginKek);

            Assert.Pass();
        }


        [Test]
        public void TokenBase2Pass()
        {
            // Pre-requisites
            var loginKek = YFByteArray.GetRndByteArray(16); // Pre-existing

            // Create a new application and link the first client to it

            // First create a new app. The new app will contain a new pair of
            // (app-kek,app-dek). Both are encrypted with the loginKek and
            // can later be retrieved with the loginKek.
            //
            var appToken = AppKeyManager.CreateApplication("chat", loginKek);

            // Now create a mapping from a client device/app to the application token above

            // First get the applicationKek
            var applicationKek = AppKeyManager.LoginGetApplicationDek(appToken, loginKek);

            // Now create the mapping
            var (cookie2, clientToken) = AppClientTokenManager.CreateClientToken(appToken.id, applicationKek);

            var appKek2 = AppClientTokenManager.GetApplicationDek(clientToken, cookie2);

            if (YFByteArray.EquiByteArrayCompare(applicationKek, appKek2) == false)
            {
                Assert.Fail();
                return;
            }

            // The two cookies / keys to give to the client are:
            //   clientToken.TokenId ["Token"]
            //   cookie2             ["Half"]
        }

    }
}
