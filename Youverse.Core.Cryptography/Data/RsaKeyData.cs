
using System;
using Youverse.Core.Cryptography.Crypto;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Youverse.Core.Cryptography.Data
{
    public class RsaPublicKeyData
    {
        public byte[] publicKey { get; set; }
        public UInt32 crc32c { get; set; }       // The CRC32C of the public key
        public UInt64 expiration { get; set; }   // Time when this key expires


        public string publicPem()
        {
            // Either -- BEGIN RSA PUBLIC KEY -- and ExportRSAPublicKey
            // Or use -- BEGIN PUBLIC KEY -- and ExportSubjectPublicKeyInfo
            return "-----BEGIN PUBLIC KEY-----\n" + publicDerBase64() + "\n-----END PUBLIC KEY-----";
        }

        public string publicDerBase64()
        {
            // Either -- BEGIN RSA PUBLIC KEY -- and ExportRSAPublicKey
            // Or use -- BEGIN PUBLIC KEY -- and ExportSubjectPublicKeyInfo
            return Convert.ToBase64String(publicKey);
        }

        public static byte[] decodePublicPem(string pem)
        {
            string publicKeyPEM = pem.Replace("-----BEGIN PUBLIC KEY-----", "")
                                        .Replace("\n", "")
                                        .Replace("\r", "")
                                        .Replace("-----END PUBLIC KEY-----", "");

            return Convert.FromBase64String(publicKeyPEM);
        }

        public static UInt32 KeyCRC(byte[] keyDerEncoded)
        {
            return CRC32C.CalculateCRC32C(0, keyDerEncoded);
        }

        public UInt32 KeyCRC()
        {
            return KeyCRC(publicKey);
        }

        // Encrypt with the public key
        public byte[] Encrypt(byte[] data)
        {
            var publicKeyRestored = PublicKeyFactory.CreateKey(publicKey);

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(true, publicKeyRestored);
            var cipherData = cipher.DoFinal(data);

            return cipherData;
        }

        public bool IsExpired()
        {
            UInt64 t = DateTimeExtensions.UnixTimeSeconds();
            if (t > expiration)
                return true;
            else
                return false;
        }


        // Not expired, it's still good (it may be overdue for a refresh)
        public bool IsValid()
        {
            return !IsExpired();
        }
    }


    public class RsaFullKeyData : RsaPublicKeyData
    {
        public SecureKey privateKey { get; set; }
        public UInt64 createdTimeStamp { get; set; } // Time when this key was created, expiration is on the public key


        public RsaFullKeyData() // Do not create with this
        {
            // Do nothing is deserialized via LiteDB
        }

        public RsaFullKeyData(int hours, int minutes = 0, int seconds = 0)
        {
            // Generate with BC an asymmetric key with BC, 2048 bits
            RsaKeyPairGenerator r = new RsaKeyPairGenerator();
            r.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
            AsymmetricCipherKeyPair keys = r.GenerateKeyPair();

            // Extract the public and the private keys
            var privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keys.Private);
            var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keys.Public);

            // Save the DER encoded private and public keys in our own data structure
            this.createdTimeStamp = DateTimeExtensions.UnixTimeSeconds();
            this.privateKey = new SecureKey(privateKeyInfo.GetDerEncoded());

            this.publicKey = publicKeyInfo.GetDerEncoded();
            this.crc32c = this.KeyCRC();
            this.expiration = this.createdTimeStamp + (UInt64)hours * 3600 + (UInt64)minutes * 60 + (UInt64)seconds;

            if (this.expiration <= this.createdTimeStamp)
                throw new Exception("Expiration must be > 0");
        }

        /// <summary>
        /// This is right now a hack for TESTING only. If this is needed then
        /// we should work that out with CreateKey()
        /// </summary>
        /// </summary>
        /// <param name="key"></param>
        /// <param name="derEncodedPrivateKey"></param>
        public RsaFullKeyData(byte[] derEncodedFullKey)
        {
            privateKey = new SecureKey(derEncodedFullKey);
            //var pkRestored = PublicKeyFactory.CreateKey(derEncodedFulKey);
            //var pk = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pkRestored);
            //publicKey = pk.GetDerEncoded();
        }



        // privatePEM needs work in case it's encrypted
        public string privatePem()
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return "-----BEGIN PRIVATE KEY-----\n" + privateDerBase64() + "\n-----END PRIVATE KEY-----";
        }

        public string privateDerBase64()
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return Convert.ToBase64String(privateKey.GetKey());
        }

        public byte[] Decrypt(byte[] cipherData)
        {
            var privateKeyRestored = PrivateKeyFactory.CreateKey(privateKey.GetKey());

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(false, privateKeyRestored);

            var clearData = cipher.DoFinal(cipherData);

            return clearData;
        }

        // If more than twice the longevity beyond the expiration, or at most 24 hours beyond expiration, 
        // then the key is considered dead and will be removed
        public bool IsDead()
        {
            UInt64 t = DateTimeExtensions.UnixTimeSeconds();
            UInt64 d = Math.Min(2 * (expiration - createdTimeStamp), 3600 * 24) + createdTimeStamp;

            if (t > d)
                return true;
            else
                return false;
        }
    }

    // Encrypted private key below here

    public class RsaPrivateKeyEncryptedData
    {
        public byte[] encryptedPrivateKey { get; set; }
        public UInt64 createdTimeStamp { get; set; } // UTC time when this key was created
        public byte[] iv { get; set; }           // The IV used for encrypting this key
    }


    public class RsaFullKeyEncryptedData : RsaPublicKeyData
    {
        public RsaPrivateKeyEncryptedData encryptedPrivateKeyData;
    }


    /*    public class RsaKeyData
        {
            // public byte[] publicKey;
            // public byte[] privateKey;   // Can we allow it to be encrypted?
            // public UInt32 crc32c;       // The CRC32C of the public key
            // public UInt64 expiration;   // Time when this key expires
            // public UInt64 instantiated; // Time when this key was made available
            // public Guid iv;             // If encrypted, this will hold the IV
            // public bool encrypted;      // If false then privateKey is the XML, otherwise it's AES-CBC base64 encrypted

            public byte[] publicKey { get; set; }
            public byte[] privateKey{ get; set; }   // Can we allow it to be encrypted?
            public UInt32 crc32c { get; set; }       // The CRC32C of the public key
            public UInt64 expiration { get; set; }   // Time when this key expires
            public UInt64 instantiated { get; set; } // Time when this key was made available
            public Guid iv { get; set; }             // If encrypted, this will hold the IV
            public bool encrypted { get; set; }      // If false then privateKey is the XML, otherwise it's AES-CBC base64 encrypted
        }*/
}