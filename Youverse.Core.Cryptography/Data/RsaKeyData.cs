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
        public byte[] publicKey { get; set; }    // DER encoded public key
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

        public void Extend(int hours = 1 )
        {
            expiration = DateTimeExtensions.UnixTimeSeconds() + (UInt64)hours * 60 * 60;
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


    // ===========================================
    // NEW SHOT AT RSA KEY. ALWAYS ENCRYPTED. PASS CONSTANT FOR NON-ENCRYPTED. IS THAT NICE?
    // ===========================================

    public class RsaFullKeyData : RsaPublicKeyData
    {
        private SensitiveByteArray _privateKey;  // Cached decrypted private key, not stored

        public byte[] storedKey { get; set; }  // The key as stored on disk encrypted with a secret key or constant

        public byte[] Iv { get; set; }  // Iv used for encrypting the storedKey and the masterCopy
        public byte[] KeyHash { get; set; }  // The hash of the encryption key 
        public UInt64 createdTimeStamp { get; set; } // Time when this key was created, expiration is on the public key


        /// <summary>
        /// For LiteDB read only.
        /// </summary>
        public RsaFullKeyData() 
        {
            // Do not create with this
            // Do nothing when deserialized via LiteDB
        }


        /// <summary>
        /// Use this constructor. Key is the encryption key used to encrypt the private key
        /// </summary>
        /// <param name="key">The key used to (AES) encrypt the private key</param>
        /// <param name="hours">Lifespan of the key, required</param>
        /// <param name="minutes">Lifespan of the key, optional</param>
        /// <param name="seconds">Lifespan of the key, optional</param>
        public RsaFullKeyData(ref SensitiveByteArray key, int hours, int minutes = 0, int seconds = 0)
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

            CreatePrivate(ref key, privateKeyInfo.GetDerEncoded());  // TODO: Can we cleanup the generated key?

            this.publicKey = publicKeyInfo.GetDerEncoded();
            this.crc32c = this.KeyCRC();
            this.expiration = this.createdTimeStamp + (UInt64)hours * 3600 + (UInt64)minutes * 60 + (UInt64)seconds;

            if (this.expiration <= this.createdTimeStamp)
                throw new Exception("Expiration must be > 0");
        }

        /// <summary>
        /// Hack used only for TESTING.
        /// </summary>
        public RsaFullKeyData(ref SensitiveByteArray key, byte[] derEncodedFullKey)
        {
            // ONLY USE FOR TESTING. DOES NOT CREATE PUBLIC KEY PROPERLY
            CreatePrivate(ref key, derEncodedFullKey);

            //_privateKey = new SensitiveByteArray(derEncodedFullKey);
            // createdTimeStamp = DateTimeExtensions.UnixTimeSeconds();
            //var pkRestored = PublicKeyFactory.CreateKey(derEncodedFulKey);
            //var pk = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pkRestored);
            //publicKey = pk.GetDerEncoded();
        }



        private void CreatePrivate(ref SensitiveByteArray key, byte[] fullDerKey)
        {
            this.Iv = ByteArrayUtil.GetRndByteArray(16);
            this.KeyHash = YouSHA.ReduceSHA256Hash(key.GetKey());
            this._privateKey = new SensitiveByteArray(fullDerKey);
            this.storedKey = AesCbc.Encrypt(this._privateKey.GetKey(), ref key, this.Iv);
        }


        private ref SensitiveByteArray GetFullKey(ref SensitiveByteArray key)
        {
            if (ByteArrayUtil.EquiByteArrayCompare(KeyHash, YouSHA.ReduceSHA256Hash(key.GetKey())) == false)
                throw new Exception("Incorrect key");

            if (_privateKey == null)
            {
                _privateKey = new SensitiveByteArray(AesCbc.Decrypt(storedKey, ref key, Iv));
            }

            return ref _privateKey;
        }

        // privatePEM needs work in case it's encrypted
        public string privatePem(ref SensitiveByteArray key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return "-----BEGIN PRIVATE KEY-----\n" + privateDerBase64(ref key) + "\n-----END PRIVATE KEY-----";
        }

        public string privateDerBase64(ref SensitiveByteArray key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            var pk = GetFullKey(ref key);
            return Convert.ToBase64String(pk.GetKey());
        }

        public byte[] Decrypt(ref SensitiveByteArray key, byte[] cipherData)
        {
            var pk = GetFullKey(ref key);

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


