using System;
using System.Security.Cryptography;
using NUnit.Framework;
using Youverse.Core.Cryptography.Data;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1;
using Youverse.Core.Cryptography.Utility;
using System.Text;

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

        [Test]
        public void Wehave9minutes()
        {
        
            // Generate an asymmetric key with BC
            RsaKeyPairGenerator r = new RsaKeyPairGenerator();
            r.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
            AsymmetricCipherKeyPair keys = r.GenerateKeyPair();

            var privInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keys.Private);
            var pubInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keys.Public);
            
            var privateKeyDer = privInfo.GetDerEncoded();
            var publicKeyDer  = pubInfo.GetDerEncoded();

            var publicKeyRestored = PublicKeyFactory.CreateKey(publicKeyDer);
            var privateKeyRestored = PrivateKeyFactory.CreateKey(privateKeyDer);

            // Go to this site:
            // https://lapo.it/asn1js/
            // To test out the key. In the immediate window CTRL+ALT+I
            // get the value of ?pubInfo and of Convert.ToBase64String(publicKeyDer). 
            // Both can be pasted into the site to compare.

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(true, publicKeyRestored);

            var testStr = "En frøk ræv";
            var dataToEncrypt = Encoding.UTF8.GetBytes(testStr);
            var cipherBlock = cipher.DoFinal(dataToEncrypt);

            // Now let's try to decrypt it

            var cipher2 = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher2.Init(false, privateKeyRestored);

            var roundTrip = cipher2.DoFinal(cipherBlock);
            string chkStr = Encoding.UTF8.GetString(roundTrip);

            if (chkStr == testStr)
                Assert.Pass();
            else
                Assert.Fail();

        }

    }
}
