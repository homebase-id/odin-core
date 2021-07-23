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
    public class TestPasswordKeyManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        [Test]
        // Rough test, hard to build a super case with random salts :o)
        public void CreateInitialPasswordKeyPass()
        {
            var np = NoncePackage.NewRandomNonce();

            // Hash the user password + user salt
            var SanityHashPassword = KeyDerivation.Pbkdf2("EnSøienØ", Convert.FromBase64String(np.SaltPassword64), KeyDerivationPrf.HMACSHA256, 100000, 16);

            var pr = PasswordKeyManagement.CalculatePasswordReply("EnSøienØ", np); // Sanity check
            if (pr.HashedPassword64 != Convert.ToBase64String(SanityHashPassword))  // Sanity check
                Assert.Fail();

            var SanityHashKek = KeyDerivation.Pbkdf2("EnSøienØ", Convert.FromBase64String(np.SaltKek64), KeyDerivationPrf.HMACSHA256, 100000, 16); // Sanity check
            if (pr.KeK64 != Convert.ToBase64String(SanityHashKek))  // Sanity check
                Assert.Fail();

            PasswordKey pk = PasswordKeyManagement.SetInitialPassword(np, pr);

            if (YFByteArray.EquiByteArrayCompare(pk.HashPassword, Convert.FromBase64String(pr.HashedPassword64)) == false)
                Assert.Fail();

            Assert.Pass();
        }


        [Test]
        // Rigged test with pre-computed constants
        public void CreateInitialPasswordKeyConstantPass()
        {
            var np = NoncePackage.NewRandomNonce();

            np.SaltPassword64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8,  9, 10, 11, 12, 13, 14, 15, 16 });
            np.SaltKek64      = Convert.ToBase64String(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 });

            var resultPasswordArray = new byte[] { 162, 146, 244, 243, 106, 138, 115, 194, 11, 233, 94, 27, 79, 215, 36, 204 };  // from asmCrypto
            var resultKekArray = new byte[] { 162, 146, 244, 243, 106, 138, 115, 194, 11, 233, 94, 27, 79, 215, 36, 204 };  // from asmCrypto

            var pr = PasswordKeyManagement.CalculatePasswordReply("EnSøienØ", np); // Sanity check
            PasswordKey pk = PasswordKeyManagement.SetInitialPassword(np, pr);

            if (YFByteArray.EquiByteArrayCompare(pk.HashPassword, resultPasswordArray) == false)
                Assert.Fail();

            var SanityHashKek = KeyDerivation.Pbkdf2("EnSøienØ", Convert.FromBase64String(np.SaltKek64), KeyDerivationPrf.HMACSHA256, 100000, 16); // Sanity check
            if (pr.KeK64 != Convert.ToBase64String(SanityHashKek))  // Sanity check
                Assert.Fail();

            Assert.Pass();
        }
    }
}
