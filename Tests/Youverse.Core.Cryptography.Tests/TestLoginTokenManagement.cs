using System;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Cryptography.Tests
{
    public class TestLoginTokenManagement
    {
        [SetUp]
        public void Setup()
        {
        }



        /// <summary>
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

            var listRsa = RsaKeyListManagement.CreateRsaKeyList(2);
            
            NonceData nonce = NonceData.NewRandomNonce(RsaKeyListManagement.GetCurrentKey(ref listRsa, out var _));

            // Pre-requisites, using the salt values from a fresh generated random Nonce
            var HashedPassword = KeyDerivation.Pbkdf2(password, Convert.FromBase64String(nonce.SaltPassword64), KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
            var KeK = KeyDerivation.Pbkdf2(password, Convert.FromBase64String(nonce.SaltKek64), KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);

            IPasswordReply rp = LoginKeyManager.CalculatePasswordReply(password, nonce);            

            // Server generates Login Authentication Token in DB and cookies for client.
            var (halfCookie, loginToken) = LoginTokenManager.CreateLoginToken(nonce, rp, listRsa);

            var testKek = LoginTokenManager.GetLoginKek(loginToken, halfCookie);

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
