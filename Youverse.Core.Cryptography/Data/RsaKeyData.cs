
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

        public static RsaPublicKeyData FromDerEncodedPublicKey(byte[] derEncodedPublicKey, int hours = 1 )
        {
            var publicKey = new RsaPublicKeyData()
            {
                publicKey = derEncodedPublicKey,
                crc32c = RsaPublicKeyData.KeyCRC(derEncodedPublicKey),
                expiration = DateTimeExtensions.UnixTimeSeconds() + (UInt64) hours * 60 * 60
            };

            return publicKey;
        }

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
        public byte[] privateKey { get; set; }  // Ought be a secureKey
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
            this.privateKey = privateKeyInfo.GetDerEncoded();

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
            // ONLY USE FOR TESTING. DOES NOT CREATE PUBLIC KEY PROPERLY
            privateKey = derEncodedFullKey;
            createdTimeStamp = DateTimeExtensions.UnixTimeSeconds();
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
            return Convert.ToBase64String(privateKey);
        }

        public byte[] Decrypt(byte[] cipherData)
        {
            var privateKeyRestored = PrivateKeyFactory.CreateKey(privateKey);

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


    // ===========================================
    // NEW SHOT AT RSA KEY. ALWAYS ENCRYPTED. PASS CONSTANT FOR NON-ENCRYPTED. IS THAT NICE?
    // ===========================================

    public class RsaFullKeyData2 : RsaPublicKeyData
    {
        private SensitiveByteArray _privateKey;  // Cached decrypted private key, not stored

        public byte[] storedKey { get; set; }  // The key as stored on disk encrypted with a secret key or constant

        public byte[] Iv { get; set; }  // Iv used for encrypting the storedKey and the masterCopy
        public byte[] KeyHash { get; set; }  // The hash of the encryption key 
        public byte[] masterKeyEncryptedKey { get; set; }  // The master key encrypted key that used to encrypt the storedKey
        public UInt64 createdTimeStamp { get; set; } // Time when this key was created, expiration is on the public key


        public RsaFullKeyData2() // Do not create with this
        {
            // Do nothing when deserialized via LiteDB
        }


        private void CreatePrivate(SensitiveByteArray masterKey, SensitiveByteArray key, byte[] fullDerKey)
        {
            this.Iv = ByteArrayUtil.GetRndByteArray(16);
            this.KeyHash = YouSHA.ReduceSHA256Hash(key.GetKey());
            this.masterKeyEncryptedKey = AesCbc.EncryptBytesToBytes_Aes(key.GetKey(), masterKey.GetKey(), this.Iv);
            this._privateKey = new SensitiveByteArray(fullDerKey);
            this.storedKey = AesCbc.EncryptBytesToBytes_Aes(this._privateKey.GetKey(), key.GetKey(), this.Iv);
        }


        public SensitiveByteArray GetKeyWithMasterKey(SensitiveByteArray masterKey)
        {
            var key = new SensitiveByteArray(AesCbc.DecryptBytesFromBytes_Aes(masterKeyEncryptedKey, masterKey.GetKey(), this.Iv));
 
            return key;
        }

        private SensitiveByteArray GetFullKey(SensitiveByteArray key)
        {
            if (ByteArrayUtil.EquiByteArrayCompare(KeyHash, YouSHA.ReduceSHA256Hash(key.GetKey())) == false)
                throw new Exception("Incorrect key");

            if (_privateKey == null)
            {
                _privateKey = new SensitiveByteArray(AesCbc.DecryptBytesFromBytes_Aes(storedKey, key.GetKey(), Iv));
            }

            return _privateKey;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="masterKey">The identity's master key, used to encrypt the key</param>
        /// <param name="key">The key used to (AES) encrypt the storedKey</param>
        /// <param name="hours"></param>
        /// <param name="minutes"></param>
        /// <param name="seconds"></param>
        public RsaFullKeyData2(SensitiveByteArray masterKey, SensitiveByteArray key, int hours, int minutes = 0, int seconds = 0)
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

            CreatePrivate(masterKey, key, privateKeyInfo.GetDerEncoded());  // TODO: Can we cleanup the generated key?

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
        public RsaFullKeyData2(SensitiveByteArray masterKey, SensitiveByteArray key, byte[] derEncodedFullKey)
        {
            // ONLY USE FOR TESTING. DOES NOT CREATE PUBLIC KEY PROPERLY
            CreatePrivate(masterKey, key, derEncodedFullKey);
            //_privateKey = new SensitiveByteArray(derEncodedFullKey);
            // createdTimeStamp = DateTimeExtensions.UnixTimeSeconds();
            //var pkRestored = PublicKeyFactory.CreateKey(derEncodedFulKey);
            //var pk = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pkRestored);
            //publicKey = pk.GetDerEncoded();
        }



        // privatePEM needs work in case it's encrypted
        public string privatePem(SensitiveByteArray key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return "-----BEGIN PRIVATE KEY-----\n" + privateDerBase64(key) + "\n-----END PRIVATE KEY-----";
        }

        public string privateDerBase64(SensitiveByteArray key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            var pk = GetFullKey(key);
            return Convert.ToBase64String(pk.GetKey());
        }

        public byte[] Decrypt(SensitiveByteArray key, byte[] cipherData)
        {
            var pk = GetFullKey(key);

            var privateKeyRestored = PrivateKeyFactory.CreateKey(pk.GetKey());

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
}


