using System;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Identity;

namespace Odin.Core.Cryptography.Tests
{
    public class TestLoginTokenManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        /// <summary>
        /// This is an example of how to handle password based Login between the client and the server
        /// 
        /// On the client you store: 
        ///    halfCookie   - HTTP ONLY, Secure (medium) cookie
        ///    loginToken   - HTTP ONLY, Secure (medium) cookie
        ///    sharedSecret - Secure local storage
        ///    
        /// Thus, you'd need to both steal the cookies AND steal the local
        /// storage to effectively hijack a session.
        /// </summary>
        [Test]
        public void TokenLoginBasePass()
        {
            string password = "EnSøienØ";

            // The server always has a list of login Ecc keys (usually with 24 hours duration per key)
            var listEcc = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, 2,
                EccKeyListManagement.DefaultHoursOfflineKey);

            // The user now enters his / her password.

            // As soon as the user clicks login, the client calls the server to get a nonce package.
            // The reply will include the Ecc key that the client should use.

            // The server receives the nonce request and first finds its current Ecc key. 
            // If there are no Ecc keys then this call to GetCurrentKey will automatically create one.
            var currentKey = EccKeyListManagement.GetCurrentKey(listEcc);

            // (If the list was updated, the server needs to save it), i.e. the out var _ shouldn't be ignored

            // The server now generates the NonceData 
            NonceData nonce = NonceData.NewRandomNonce(currentKey);

            // The server sends the nonce data back to the client

            // The client has now received the nonce package and based on the previously entered user password, 
            // the client can calculate the data that the server will need:

            // Pre-requisites, using the salt values from a fresh generated random Nonce:

            // The client calulates the data to send to the server
            // This is a temporary Ecc on the client
            var clientEcc = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

            PasswordReply rp = PasswordDataManager.CalculatePasswordReply(password, nonce, clientEcc);
            // The client sends the reply to the server

            // The server receives the reply
            // Server generates Login Authentication Token in DB and cookies for client.

            // These two values were already pre-existing on the server. The HashedPassword was set when the
            // password was initially set. And the KeK was also calculated at that time.
            //
            var HashedPassword = KeyDerivation.Pbkdf2(password, Convert.FromBase64String(nonce.SaltPassword64), KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
            var KeK = KeyDerivation.Pbkdf2(password, Convert.FromBase64String(nonce.SaltKek64), KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);

            // The server now parses the received reply and creates the tokens needed for the client/server.
            var (halfCookie, loginToken) = OwnerConsoleTokenManager.CreateToken((OdinId)"someone.com", nonce, rp, listEcc);

            var testKek = OwnerConsoleTokenManager.GetMasterKey(loginToken, halfCookie);

            if (ByteArrayUtil.EquiByteArrayCompare(KeK, testKek.GetKey()) == false)
            {
                Assert.Fail();
                return;
            }

            Assert.Pass();
        }

        [Test]
        public void TokenLoginBaseDualPass()
        {
            // Pre-requisites
            // var loginKek = YFByteArray.GetRndByteArray(16); // Pre-existing
            // var sharedSecret = YFByteArray.GetRndByteArray(16); // Pre-existing
            // var login2Kek = YFByteArray.GetRndByteArray(16); // Pre-existing
            // var shared2Secret = YFByteArray.GetRndByteArray(16); // Pre-existing
            //
            // // Server generates Login Authentication Token in DB and cookies for client.
            // var (halfCookie, loginToken) = LoginTokenManager.CreateLoginToken(loginKek, sharedSecret);
            // var (half2Cookie, login2Token) = LoginTokenManager.CreateLoginToken(login2Kek, shared2Secret);
            //
            // var testKek = LoginTokenManager.GetLoginKek(loginToken, halfCookie);
            // var test2Kek = LoginTokenManager.GetLoginKek(login2Token, half2Cookie);
            //
            // if (YFByteArray.EquiByteArrayCompare(loginKek, testKek) == false)
            // {
            //     Assert.Fail();
            //     return;
            // }
            //
            // if (YFByteArray.EquiByteArrayCompare(login2Kek, test2Kek) == false)
            // {
            //     Assert.Fail();
            //     return;
            // }
            //
            // Assert.Pass();
        }
    }
}