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
            // Generate our RSAKey data object
            
            var rsa = new RsaKeyData();
            rsa.encrypted = false;
            rsa.iv = Guid.Empty;
            rsa.privateKey = privInfo.GetDerEncoded();
            rsa.publicKey  = pubInfo.GetDerEncoded();
            //rsa.crc32c = KeyCRC(rsa);
            rsa.instantiated = DateTimeExtensions.UnixTime();
            //rsa.expiration = rsa.instantiated + (UInt64)hours * 3600 + (UInt64)minutes * 60 + (UInt64)seconds;
            
            
            
        }

    }
}
