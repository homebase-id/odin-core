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

        //
        // ===== A FEW BYTE ARRAY TESTS =====
        //

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

        //
        // ===== AES CBC TESTS =====
        //

        [Test]
        public void AesCbcTextPass()
        {
            var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            string testData = "The quick red fox";

            var (IV, cipher) = AesCbc.EncryptStringToBytes_Aes(testData, key);
            var roundtrip = AesCbc.DecryptStringFromBytes_Aes(cipher, key, IV);

            if (roundtrip == testData)
                Assert.Pass();
            else
                Assert.Fail();
        }

        [Test]
        public void AesCbcPass()
        {
            if (AesCbc.TestPrivate())
                Assert.Pass();
            else
                Assert.Fail();
        }

        //
        // ===== PACKET TESTS =====
        //
        [Test]
        public void HostToHostPacketPass()
        {
            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            rsaGenKeys.PersistKeyInCsp = false; // WHOA?! Figure out if a key is saved anywhere?!
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            // Data to encrypt
            string mySecret = "hello wørld";
            byte[] payload = Encoding.UTF8.GetBytes(mySecret);

            var (rsaHeader, encryptedPayload) = HostToHost.EncryptRSAPacket(payload, publicXml);

            // Now imagine we're at the recipient host:
            var copyPayload = HostToHost.DecryptRSAPacket(rsaHeader, encryptedPayload, privateXml);

            string copySecret = Encoding.UTF8.GetString(copyPayload);

            if (copySecret == mySecret)
                Assert.Pass();
            else
                Assert.Fail();
        }


        [Test]
        public void CrcPass()
        {
            // CRC
            var crc = CRC32C.CalculateCRC32C(0, Encoding.ASCII.GetBytes("bear sandwich"));

            if (crc == 3711466352)
                Assert.Pass();
            else
                Assert.Fail();
        }


        //
        // ===== SPEED TESTS =====
        //
        [Test]
        public void RSASpeedPass()
        {
            int i;

            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            rsaGenKeys.PersistKeyInCsp = false; // WHOA?! Figure out if a key is saved anywhere?!
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            for (i=0; i < 600; i++)
            {
                RSACryptoServiceProvider tmpKey = new RSACryptoServiceProvider(2048);
                tmpKey.FromXmlString(privateXml);
            }

            Assert.Pass();
        }



        /// <summary>
        /// This test illustrates how to take a host to host package, with a RSA header,
        /// and then transform the RSA header into the AES header (for local storage).
        /// </summary>
        [Test]
        public void HostToHostPacketHeaderTransformPass()
        {
            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            rsaGenKeys.PersistKeyInCsp = false; // WHOA?! Figure out if a key is saved anywhere?!
            string privateXml = rsaGenKeys.ToXmlString(true);
            string publicXml = rsaGenKeys.ToXmlString(false);

            // Data to encrypt
            string mySecret = "hello wørld";
            byte[] payload = Encoding.UTF8.GetBytes(mySecret);

            var (rsaHeader, encryptedPayload) = HostToHost.EncryptRSAPacket(payload, publicXml);

            var sharedSecret = YFByteArray.GetRndByteArray(16);
            var aesHeader = HostToHost.TransformRSAtoAES(rsaHeader, privateXml, sharedSecret);
            // var (iv, keyEncrypted) = HostToHost.TransformRSAtoAES(rsaHeader, privateXml, sharedSecret);

            // Now let's see if we can decode the header
            var (randomIv2, encryptedUnlockHeader) = HostToHost.ParseAesHeader(aesHeader);
            var unlockHeader = AesCbc.DecryptBytesFromBytes_Aes(encryptedUnlockHeader, sharedSecret, randomIv2);
            var (key, iv) = HostToHost.ParseUnlockHeader(unlockHeader);
            var data = AesCbc.DecryptBytesFromBytes_Aes(encryptedPayload, key, iv);

            string originalResult = Encoding.UTF8.GetString(data);

            if (originalResult == mySecret)
                Assert.Pass();
            else
                Assert.Fail();
        }



        //
        // ===== RSA TESTS =====
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
        // ===== PBKDF2 TESTS =====
        //

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
