using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DotYou.Kernel.Cryptography;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;

namespace DotYou.Kernel.CryptographyTests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void GenerateRndPass()
        {
            byte[] ba = YFByteArray.GetRndByteArray(40);

            if (ba.Length == 40)
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void CompareTwoRndFail()
        {
            byte[] ba1 = YFByteArray.GetRndByteArray(40);
            byte[] ba2 = YFByteArray.GetRndByteArray(40);

            if (YFByteArray.EquiByteArrayCompare(ba1, ba2))
                Assert.Fail();
            else
                Assert.Pass();
        }

        [Test]
        public void CompareArrayPass()
        {
            byte[] ba1 = YFByteArray.GetRndByteArray(40);
 
            if (YFByteArray.EquiByteArrayCompare(ba1, ba1))
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void XorPass()
        {
            byte[] ba1 = YFByteArray.GetRndByteArray(40);
            byte[] ba2 = YFByteArray.GetRndByteArray(40);

            if (YFByteArray.EquiByteArrayCompare(ba1, ba2))
                Assert.Fail();

            var ra = YFByteArray.EquiByteArrayXor(ba1, ba2);

            if (YFByteArray.EquiByteArrayCompare(ra, ba1))
                Assert.Fail();

            if (YFByteArray.EquiByteArrayCompare(ra, ba2))
                Assert.Fail();

            var fa = YFByteArray.EquiByteArrayXor(ra, ba2);

            if (YFByteArray.EquiByteArrayCompare(fa, ba1))
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void CreateKeyDerivationPass()
        {
            // var asalt = YFByteArray.GetRndByteArray(10);
            // var ba1 = YFByteArray.CreateKeyDerivationKey(asalt, "mypassword", 32);

            Assert.Fail();
        }

        [Test]
        public void PasswordTestPass()
        {
            string s;

            //var asalt = YFByteArray.GetRndByteArray(8);
            //s = YFByteArray.PasswordFlow("Mit password", asalt);

            Assert.Fail();
        }

        [Test]
        public void SetRawTestPass()
        {
            IdentityKeySecurity k = new IdentityKeySecurity();

            k.SetRawPassword("a");

            // Hash the user password + user salt
            var HashPassword = KeyDerivation.Pbkdf2("a", k.SaltPassword, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
            var KeyEncryptionKey = KeyDerivation.Pbkdf2("a", k.SaltKek, KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);

            // Decrypt the bytes to a string.
            var data = Convert.FromBase64String(k.EncryptedPrivateKey);
            string roundtrip = YFRijndaelWrap.DecryptStringFromBytes(data, KeyEncryptionKey, k.SaltPassword);

            //Display the original data and the decrypted data.
            // Console.WriteLine("Original:   {0}", original);
            Console.WriteLine("Round Trip: {0}", roundtrip);

            Assert.Pass();
        }



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

        [Test]
        public void Pbkdf2TestPass()
        {
            var saltArray = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var resultArray = new byte[] { 162, 146, 244, 243, 106, 138, 115, 194, 11, 233, 94, 27, 79, 215, 36, 204 };  // from asmCrypto

            // Hash the user password + user salt
            var HashPassword = KeyDerivation.Pbkdf2("EnSøienØ", saltArray, KeyDerivationPrf.HMACSHA256, 100000, 16);

            if (YFByteArray.EquiByteArrayCompare(HashPassword, resultArray))
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
            var HashPassword = KeyDerivation.Pbkdf2("EnSøienØ", saltArray, KeyDerivationPrf.HMACSHA256, 100000, 16);
            sw.Stop();

            Console.WriteLine("Elapsed={0}", sw.Elapsed);

            Assert.Pass();
        }
    }
}