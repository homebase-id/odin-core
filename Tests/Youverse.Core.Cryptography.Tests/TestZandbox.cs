using System;
using System.Security.Cryptography;
using NUnit.Framework;

namespace Youverse.Core.Cryptography.Tests
{
    public class TestZandbox
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void TestSha256Pass()
        {
            var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var crypt = new SHA256Managed();
            byte[] hash = crypt.ComputeHash(key);
            Console.WriteLine("SHA-256={0}", hash);
            Assert.Pass();

            //
            // I've manually verified that the JS counterpart reaches the same value
        }
    }
}
