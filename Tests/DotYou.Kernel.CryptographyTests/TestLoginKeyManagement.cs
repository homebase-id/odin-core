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
            var hostRsa = new RsaKeyList(2);
            RsaKeyManagement.generateNewKey(hostRsa, 24);

            // Client requests a noncePackage from the server (after password is entered)
            var np = NoncePackage.NewRandomNonce(RsaKeyManagement.GetCurrentPublicKeyPem(hostRsa));

            // Client calculates the passwordReply based on the password and noncePackage
            var pr = LoginKeyManagement.CalculatePasswordReply("EnSøienØ", np);

            // Server receives the passwordReply and set's the user's initial password
            PasswordKey pk = LoginKeyManagement.SetInitialPassword(np, pr, hostRsa);

            Assert.Pass();
        }


        [Test]
        public void NewLoginTest2KeysPass()
        {
            // Generate Host RSA key
            var hostRsa = new RsaKeyList(2);
            RsaKeyManagement.generateNewKey(hostRsa, 24);

            var np = NoncePackage.NewRandomNonce(RsaKeyManagement.GetCurrentPublicKeyPem(hostRsa));

            RsaKeyManagement.generateNewKey(hostRsa, 24);

            var pr = LoginKeyManagement.CalculatePasswordReply("EnSøienØ", np); // Sanity check

            PasswordKey pk = LoginKeyManagement.SetInitialPassword(np, pr, hostRsa);

            Assert.Pass();
        }


        [Test]
        // Rough test, hard to build a super case with random salts :o)
        public void CreateInitialPasswordKeyPass()
        {
            // Generate Host RSA key 
            var hostRsa = new RsaKeyList(2);
            RsaKeyManagement.generateNewKey(hostRsa, 24);

            var np = NoncePackage.NewRandomNonce(RsaKeyManagement.GetCurrentPublicKeyPem(hostRsa));

            // Sanity Values
            var SanityHashPassword = KeyDerivation.Pbkdf2("EnSøienØ", Convert.FromBase64String(np.SaltPassword64), KeyDerivationPrf.HMACSHA256, 100000, 16);
            //var SanityHashKek = KeyDerivation.Pbkdf2("EnSøienØ", Convert.FromBase64String(np.SaltKek64), KeyDerivationPrf.HMACSHA256, 100000, 16);

            var pr = LoginKeyManagement.CalculatePasswordReply("EnSøienØ", np); // Sanity check

            PasswordKey pk = LoginKeyManagement.SetInitialPassword(np, pr, hostRsa);

            if (YFByteArray.EquiByteArrayCompare(SanityHashPassword, pk.HashPassword) == false)
                Assert.Fail();

            Assert.Pass();
        }


        [Test]
        // Rigged test with pre-computed constants
        public void CreateInitialPasswordKeyConstantPass()
        {
            // Generate Host RSA key 
            var hostRsa = new RsaKeyList(2);
            RsaKeyManagement.generateNewKey(hostRsa, 24);

            var np = NoncePackage.NewRandomNonce(RsaKeyManagement.GetCurrentPublicKeyPem(hostRsa));

            np.SaltPassword64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8,  9, 10, 11, 12, 13, 14, 15, 16 });
            np.SaltKek64      = Convert.ToBase64String(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 });

            var resultPasswordArray = new byte[] { 162, 146, 244, 243, 106, 138, 115, 194, 11, 233, 94, 27, 79, 215, 36, 204 };  // from asmCrypto
            var resultKekArray = new byte[] { 162, 146, 244, 243, 106, 138, 115, 194, 11, 233, 94, 27, 79, 215, 36, 204 };  // from asmCrypto

            var pr = LoginKeyManagement.CalculatePasswordReply("EnSøienØ", np); // Sanity check
            PasswordKey pk = LoginKeyManagement.SetInitialPassword(np, pr, hostRsa);

            if (YFByteArray.EquiByteArrayCompare(pk.HashPassword, resultPasswordArray) == false)
                Assert.Fail();

            Assert.Pass();
        }
    }
}
