using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DotYou.Kernel.Cryptography;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;

namespace DotYou.Kernel.CryptographyTests
{
    public class TestRsaKeyManagement
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void RsaKeyCreateTest()
        {
            var key = RsaKeyManagement.CreateKey(1);
        }

        [Test]
        public void RsaKeyCreateInvalidTest()
        {
            try
            {
                var key = RsaKeyManagement.CreateKey(0);
            }
            catch (Exception e)
            {
                Assert.Pass();
                return;
            }

            Assert.Fail();
        }

        [Test]
        public void RsaKeyCreateTimerTest()
        {
            var key = RsaKeyManagement.CreateKey(0,seconds:2);

            if (!RsaKeyManagement.IsValid(key))
                Assert.Fail();

            if (RsaKeyManagement.IsExpired(key))
                Assert.Fail();

            if (RsaKeyManagement.IsDead(key))
                Assert.Fail();

            Thread.Sleep(3000);  // The key is now 1 second expired.

            if (!RsaKeyManagement.IsExpired(key))
                Assert.Fail();

            if (RsaKeyManagement.IsValid(key))
                Assert.Fail();

            if (RsaKeyManagement.IsDead(key))
                Assert.Fail();

            Thread.Sleep(3000); // The key is now 4 seconds expired, so dead.

            if (!RsaKeyManagement.IsExpired(key))
                Assert.Fail();

            if (RsaKeyManagement.IsValid(key))
                Assert.Fail();

            if (!RsaKeyManagement.IsDead(key))
                Assert.Fail();

            Assert.Pass();
        }



        //
        // ===== GENERIC RSA TESTS =====
        //

        [Test]
        public void RSABasicTest()
        {
            // Generate a public/private key using RSA  
            RSACryptoServiceProvider RSA = new RSACryptoServiceProvider(2048); // 2048 bits

            // Read public and private key in a string  
            string str = RSA.ToXmlString(true);
            Console.WriteLine(str);

            // Get key into parameters  
            RSAParameters RSAKeyInfo = RSA.ExportParameters(true);
            Console.WriteLine($"Modulus: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.Modulus)}");
            Console.WriteLine($"Exponent: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.Exponent)}");
            Console.WriteLine($"P: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.P)}");
            Console.WriteLine($"Q: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.Q)}");
            Console.WriteLine($"DP: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.DP)}");
            Console.WriteLine($"DQ: {System.Text.Encoding.UTF8.GetString(RSAKeyInfo.DQ)}");
        }

        [Test]
        public void RSAPublicEnrcryptDecryptTest()
        {
            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider();
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            // Data to encrypt
            string mySecret = "hello world";
            byte[] toEncryptData = Encoding.ASCII.GetBytes(mySecret);

            //Encode with public key
            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider();
            rsaPublic.FromXmlString(publicXml);
            byte[] encryptedRSA = rsaPublic.Encrypt(toEncryptData, false);
            string EncryptedResult = Encoding.Default.GetString(encryptedRSA);

            //Decode with private key
            var rsaPrivate = new RSACryptoServiceProvider();
            rsaPrivate.FromXmlString(privateXml);
            byte[] decryptedRSA = rsaPrivate.Decrypt(encryptedRSA, false);
            string originalResult = Encoding.Default.GetString(decryptedRSA);

            if (originalResult == mySecret)
                Assert.Pass();
            else
                Assert.Fail();
        }


        [Test]
        public void RSAPublicSignTest()
        {
            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            // Data to encrypt
            string mySecret = "hello world";
            byte[] toEncryptData = Encoding.ASCII.GetBytes(mySecret);

            //Sign with private key
            var rsaPrivate = new RSACryptoServiceProvider();
            rsaPrivate.FromXmlString(privateXml);
            byte[] signedRSA = rsaPrivate.SignData(toEncryptData, CryptoConfig.MapNameToOID("SHA256"));
            string signedResult = Encoding.Default.GetString(signedRSA);

            //Verify with public key 
            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider();
            rsaPublic.FromXmlString(publicXml);
            bool SignatureOK = rsaPublic.VerifyData(toEncryptData, CryptoConfig.MapNameToOID("SHA256"), signedRSA);

            if (SignatureOK)
                Assert.Pass();
            else
                Assert.Fail();
        }

        //
        // ===== SPEED TESTS TO MAKE SURE IMPORT DOESNT EAT GENERATE CPU =====
        //
        [Test]
        public void RSASpeedPass()
        {
            int i;

            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            rsaGenKeys.PersistKeyInCsp = false; // WHOA?! Figure out if a key is saved anywhere?!
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            for (i = 0; i < 600; i++)
            {
                RSACryptoServiceProvider tmpKey = new RSACryptoServiceProvider(2048);
                tmpKey.FromXmlString(privateXml);
            }

            Assert.Pass();
        }
    }
}
