using System;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;

namespace Odin.Core.Cryptography.Tests
{
    public class TestLoginKeyManagement
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        // Use this code for guidance on how to implement setting initial password on server & client
        public void NewLoginTestPass()
        {
            // Generate Host RSA key - on the server this key already pre-exists
            // The host RSA key is not encrypted on the server and thus the secret key
            // is accessible to the server even without a password.
            var hostRsa = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, 2, RsaKeyListManagement.DefaultHoursOfflineKey);
            RsaKeyListManagement.GenerateNewKey(RsaKeyListManagement.zeroSensitiveKey, hostRsa, RsaKeyListManagement.DefaultHoursOfflineKey);

            // Client requests a noncePackage from the server (after password is entered)
            var np = NonceData.NewRandomNonce(RsaKeyListManagement.GetCurrentKey(hostRsa));

            // Client calculates the passwordReply based on the password and noncePackage
            var pr = PasswordDataManager.CalculatePasswordReply("EnSøienØ", np);

            // Server receives the passwordReply and set's the user's initial password
            PasswordData pk = PasswordDataManager.SetInitialPassword(np, pr, hostRsa);

            Assert.Pass();
        }

        [Test]
        // Use this code for guidance on how to implement client login (e.g. new browser)
        public void ExistingLoginTestPass()
        {
            // // Generate Host RSA key - on the server this key already pre-exists
            // // The host RSA key is not encrypted on the server and thus the secret key
            // // is accessible to the server even without a password.
            // var hostRsa = RsaKeyListManagement.CreateRsaKeyList(2);
            // //RsaKeyListManagement.GenerateNewKey(hostRsa, 24);
            //
            // // Client requests a noncePackage from the server (after password is entered)
            // // The server loads the salts via GetSToredSalts() and generates the noncePackage
            // // in GenerateAuthenticationNonce() and stores it and returns it to the client
            // // We probably cannot streamline that in a call here
            //
            // // Simulate pre-generated PasswordKey values
            // string saltPassword64 = Convert.ToBase64String(YFByteArray.GetRndByteArray(CryptographyConstants.SALT_SIZE));
            // string saltKek64 = Convert.ToBase64String(YFByteArray.GetRndByteArray(CryptographyConstants.SALT_SIZE));
            //
            // var np = new NonceData(saltPassword64, saltKek64, RsaKeyListManagement.GetCurrentPublicKeyPem(hostRsa), RsaKeyListManagement.GetCurrentKey(hostRsa).crc32c);
            //
            // // Client calculates the passwordReply based on the password and noncePackage
            // // the reply includes the shared secret and is sent to the server
            // var pr = PasswordDataManager.CalculatePasswordReply("EnSøienØ", np);
            //
            // // Server receives the passwordReply and now needs to validate the password
            // var (kek, sharedsecret) = PasswordDataManager.Authenticate(np, pr, hostRsa);
            //
            // // Server generates Login Authentication Token in DB and cookies for client.
            // var (halfCookie, loginToken) = LoginTokenManager.CreateLoginToken(kek, sharedsecret);
            //
            // var checkKek = LoginTokenManager.GetLoginKek(loginToken, halfCookie);
            //
            // if (YFByteArray.EquiByteArrayCompare(checkKek, kek) == false)
            // {
            //     Assert.Fail();
            //     return;
            // }
            //
            // // Now on the client for login place these cookies (secure(medium), HTTP only):
            // //
            // // cookie["login"] = loginToken.TokenId
            // // cookie["loginkey"] = cookie2
            //
            // var calcNonce = NonceGrowingManager.CalculateBase64NonceSHA256(1, sharedsecret);
            // NonceGrowingManager.ValidateNonce(loginToken.NonceKeeper, 1, calcNonce, loginToken.SharedSecret);
            //
            // calcNonce = NonceGrowingManager.CalculateBase64NonceSHA256(7, sharedsecret);
            // NonceGrowingManager.ValidateNonce(loginToken.NonceKeeper, 7, calcNonce, loginToken.SharedSecret);
            //
            // Assert.Pass();
        }


        [Test]
        // Example of client sending two requests and how the nonce increases
        public void TwoRequestsNonceLoginTestPass()
        {
            // Generate Host RSA key - on the server this key already pre-exists
            // The host RSA key is not encrypted on the server and thus the secret key
            // is accessible to the server even without a password.
            // var hostRsa = RsaKeyListManagement.CreateRsaKeyList(2);
            // RsaKeyListManagement.GenerateNewKey(hostRsa, 24);
            //
            // // Client requests a noncePackage from the server (after password is entered)
            // // The server loads the salts via GetSToredSalts() and generates the noncePackage
            // // in GenerateAuthenticationNonce() and stores it and returns it to the client
            // // We probably cannot streamline that in a call here
            // var np = NonceData.NewRandomNonce(RsaKeyListManagement.GetCurrentKey(hostRsa));
            //
            // // Client calculates the passwordReply based on the password and noncePackage
            // // the reply includes the shared secret and is sent to the server
            // var pr = PasswordDataManager.CalculatePasswordReply("EnSøienØ", np);
            //
            // // Server receives the passwordReply and now needs to validate the password
            // var (kek, sharedsecret) = PasswordDataManager.Authenticate(np, pr, hostRsa);
            //
            // // Server generates Login Authentication Token in DB and cookies for client.
            // var (halfCookie, loginToken) = LoginTokenManager.CreateLoginToken(kek, sharedsecret);
            //
            // // Now on the client for login place these cookies (secure(medium), HTTP only):
            // //
            // // cookie["login"] = loginToken.TokenId
            // // cookie["loginkey"] = cookie2
            //
            // // Now we need to communicate with the server, do two requests and validate the nonce
            // // combined with the sharedSecret.
            // // XXX
            //
            // Assert.Pass();
        }


        [Test]
        public void NewLoginTest2KeysPass()
        {
            // Generate Host RSA key
            var hostRsa = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, 2, RsaKeyListManagement.DefaultHoursOfflineKey);
            RsaKeyListManagement.GenerateNewKey(RsaKeyListManagement.zeroSensitiveKey, hostRsa, 24);

            var np = NonceData.NewRandomNonce(RsaKeyListManagement.GetCurrentKey(hostRsa));

            RsaKeyListManagement.GenerateNewKey(RsaKeyListManagement.zeroSensitiveKey, hostRsa, RsaKeyListManagement.DefaultHoursOfflineKey);

            var pr = PasswordDataManager.CalculatePasswordReply("EnSøienØ", np); // Sanity check

            PasswordData pk = PasswordDataManager.SetInitialPassword(np, pr, hostRsa);

            Assert.Pass();
        }


        [Test]
        // Rough test, hard to build a super case with random salts :o)
        public void CreateInitialPasswordKeyPass()
        {
            // Generate Host RSA key 
            var hostRsa = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, 2, RsaKeyListManagement.DefaultHoursOfflineKey);
            RsaKeyListManagement.GenerateNewKey(RsaKeyListManagement.zeroSensitiveKey, hostRsa, 24);

            var np = NonceData.NewRandomNonce(RsaKeyListManagement.GetCurrentKey(hostRsa));

            // Sanity Values
            var SanityHashPassword = KeyDerivation.Pbkdf2("EnSøienØ", Convert.FromBase64String(np.SaltPassword64), KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS, 16);

            var pr = PasswordDataManager.CalculatePasswordReply("EnSøienØ", np); // Sanity check

            PasswordData pk = PasswordDataManager.SetInitialPassword(np, pr, hostRsa);

            if (ByteArrayUtil.EquiByteArrayCompare(SanityHashPassword, pk.HashPassword) == false)
                Assert.Fail();

            Assert.Pass();
        }


        [Explicit, Test]
        // Rigged test with pre-computed constants. Will only work with Iterations 100,000
        public void CreateInitialPasswordKeyConstantPass()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Not valid on non-windows OS");
                return;
            }

            // Generate Host RSA key 
            var hostRsa = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, 2, RsaKeyListManagement.DefaultHoursOfflineKey);

            var np = NonceData.NewRandomNonce(RsaKeyListManagement.GetCurrentKey(hostRsa));

            np.SaltPassword64 = Convert.ToBase64String(new byte[] {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16});
            np.SaltKek64 = Convert.ToBase64String(new byte[] {2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17});

            var resultPasswordArray = new byte[] {162, 146, 244, 243, 106, 138, 115, 194, 11, 233, 94, 27, 79, 215, 36, 204}; // from asmCrypto
            var resultKekArray = new byte[] {162, 146, 244, 243, 106, 138, 115, 194, 11, 233, 94, 27, 79, 215, 36, 204}; // from asmCrypto

            var pr = PasswordDataManager.CalculatePasswordReply("EnSøienØ", np); // Sanity check
            PasswordData pk = PasswordDataManager.SetInitialPassword(np, pr, hostRsa);

            if (ByteArrayUtil.EquiByteArrayCompare(pk.HashPassword, resultPasswordArray) == false)
                Assert.Fail();

            Assert.Pass();
        }
    }
}