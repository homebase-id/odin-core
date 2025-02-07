using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;

namespace Odin.Core.Cryptography.Tests
{
    public class TestPbkdf2Management
    {
        [SetUp]
        public void Setup()
        {
        }

        //
        // ===== PBKDF2 TESTS =====
        //

        [Test]
        public void Pbkdf2TestPass()
        {
            var saltArray = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var resultArray = new byte[] { 162, 146, 244, 243, 106, 138, 115, 194, 11, 233, 94, 27, 79, 215, 36, 204 };  // from asmCrypto

            // Hash the user password + user salt
            var HashPassword = KeyDerivation.Pbkdf2("EnSøienØ", saltArray, KeyDerivationPrf.HMACSHA256, 100000, 16);

            if (ByteArrayUtil.EquiByteArrayCompare(HashPassword, resultArray))
                Assert.Pass();
            else
                Assert.Fail();
        }


        [Test]
        public void Pbkdf2TimerPass()
        {
            var saltArray = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            Stopwatch sw = new Stopwatch();

            sw.Start();
            // Hash the user password + user salt
            var HashPassword = KeyDerivation.Pbkdf2("EnSøienØ", saltArray, KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS, 16);
            sw.Stop();

            Console.WriteLine("Elapsed={0}", sw.Elapsed);

            Assert.Pass();
        }
    }
}
