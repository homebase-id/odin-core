using System;
using System.Threading;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Time;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Odin.Core.Cryptography.Data
{
    public class RsaPublicKeyData
    {
        public byte[] publicKey { get; set; } // DER encoded public key

        public UInt32 crc32c { get; set; } // The CRC32C of the public key

        public UnixTimeUtc
            expiration
        {
            get;
            set;
        } // Time when this key expires, be aware that since this is a property, you will get a copy and using e.g. .AddHours() will add to the copy

        public static RsaPublicKeyData FromDerEncodedPublicKey(byte[] derEncodedPublicKey, int hours = 1)
        {
            var publicKey = new RsaPublicKeyData()
            {
                publicKey = derEncodedPublicKey,
                crc32c = RsaPublicKeyData.KeyCRC(derEncodedPublicKey),
                expiration = UnixTimeUtc.Now().AddSeconds(hours * 60 * 60)
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

            Interlocked.Increment(ref SimplePerformanceCounter.noRsaEncryptions);

            return cipherData;
        }

        public bool VerifySignature(byte[] dataThatWasSigned, byte[] signature)
        {
            var publicKeyRestored = PublicKeyFactory.CreateKey(publicKey);

            ISigner signer = SignerUtilities.GetSigner("SHA256withRSA");
            signer.Init(false, publicKeyRestored); // Init for verification (false), with the public key

            signer.BlockUpdate(dataThatWasSigned, 0, dataThatWasSigned.Length);

            bool isSignatureCorrect = signer.VerifySignature(signature);

            return isSignatureCorrect;
        }

        public void Extend(int hours = 1)
        {
            expiration = UnixTimeUtc.Now().AddSeconds(hours * 60 * 60);
        }

        public bool IsExpired()
        {
            if (UnixTimeUtc.Now() > expiration)
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

    public class RsaFullKeyData : RsaPublicKeyData, ICloneable
    {
        private SensitiveByteArray _privateKey; // Cached decrypted private key, not stored

        public byte[] storedKey { get; set; } // The key as stored on disk encrypted with a secret key or constant

        public byte[] Iv { get; set; } // Iv used for encrypting the storedKey and the masterCopy
        public byte[] KeyHash { get; set; } // The hash of the encryption key 

        public UnixTimeUtc
            createdTimeStamp
        {
            get;
            set;
        } // Time when this key was created, expiration is on the public key. Do NOT use a property or code will return a copy value.
        
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
        public RsaFullKeyData(SensitiveByteArray key, int hours, int minutes = 0, int seconds = 0)
        {
            // Generate with BC an asymmetric key with BC, 2048 bits
            RsaKeyPairGenerator r = new RsaKeyPairGenerator();
            r.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
            AsymmetricCipherKeyPair keys = r.GenerateKeyPair();

            // Extract the public and the private keys
            var privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keys.Private);
            var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keys.Public);

            // Save the DER encoded private and public keys in our own data structure
            this.createdTimeStamp = UnixTimeUtc.Now();
            this.expiration = this.createdTimeStamp;
            this.expiration = this.expiration.AddSeconds(hours * 3600 + minutes * 60 + seconds);
            if (this.expiration <= this.createdTimeStamp)
                throw new Exception("Expiration must be > 0");

            CreatePrivate(key, privateKeyInfo.GetDerEncoded()); // TODO: Can we cleanup the generated key?

            this.publicKey = publicKeyInfo.GetDerEncoded();
            this.crc32c = this.KeyCRC();

            Interlocked.Increment(ref SimplePerformanceCounter.noRsaKeysCreated);
        }

        /// <summary>
        /// Hack used only for TESTING.
        /// </summary>
        public RsaFullKeyData(SensitiveByteArray key, byte[] derEncodedFullKey)
        {
            // ONLY USE FOR TESTING. DOES NOT CREATE PUBLIC KEY PROPERLY
            CreatePrivate(key, derEncodedFullKey);

            //_privateKey = new SensitiveByteArray(derEncodedFullKey);
            // createdTimeStamp = DateTimeExtensions.UnixTimeSeconds();
            //var pkRestored = PublicKeyFactory.CreateKey(derEncodedFulKey);
            //var pk = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pkRestored);
            //publicKey = pk.GetDerEncoded();
            Interlocked.Increment(ref SimplePerformanceCounter.noRsaKeysCreatedTest);
        }


        private void CreatePrivate(SensitiveByteArray key, byte[] fullDerKey)
        {
            this.Iv = ByteArrayUtil.GetRndByteArray(16);
            this.KeyHash = ByteArrayUtil.ReduceSHA256Hash(key.GetKey());
            this._privateKey = new SensitiveByteArray(fullDerKey);
            this.storedKey = AesCbc.Encrypt(this._privateKey.GetKey(), key, this.Iv);
        }


        private SensitiveByteArray GetFullKey(SensitiveByteArray key)
        {
            if (ByteArrayUtil.EquiByteArrayCompare(KeyHash, ByteArrayUtil.ReduceSHA256Hash(key.GetKey())) == false)
                throw new Exception("Incorrect key");

            if (_privateKey == null)
            {
                _privateKey = new SensitiveByteArray(AesCbc.Decrypt(storedKey, key, Iv));
            }

            return _privateKey;
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

            Interlocked.Increment(ref SimplePerformanceCounter.noRsaDecryptions);

            return clearData;
        }

        // If more than twice the longevity beyond the expiration, or at most 24 hours beyond expiration, 
        // then the key is considered dead and will be removed
        public bool IsDead()
        {
            if (expiration.seconds <= 0)
                throw new Exception("Expiration has not been initialized");

            if (createdTimeStamp.seconds <= 0)
                throw new Exception("createdTimeStamp has not been initialized");

            Int64 t = UnixTimeUtc.Now().seconds;
            Int64 d = Math.Min(2 * (expiration.seconds - createdTimeStamp.seconds), 3600 * 24) + createdTimeStamp.seconds;

            if (t > d)
                return true;
            else
                return false;
        }


        /// <summary>
        /// Sign a block of data with a BC RSA key
        /// </summary>
        /// <param name="key">The key to unlock the RSA private key</param>
        /// <param name="data">The data to sign</param>
        /// <returns>The signature</returns>
        public byte[] Sign(SensitiveByteArray key, byte[] dataToSign)
        {
            var pk = GetFullKey(key);

            var privateKeyRestored = PrivateKeyFactory.CreateKey(pk.GetKey());

            // Assuming that 'keys' is your AsymmetricCipherKeyPair
            ISigner signer = SignerUtilities.GetSigner("SHA256withRSA");
            signer.Init(true, privateKeyRestored); // Init for signing (true), with the private key

            signer.BlockUpdate(dataToSign, 0, dataToSign.Length);

            byte[] signature = signer.GenerateSignature();

            return signature;
        }
        
        public object Clone()
        {
            return new RsaFullKeyData()
            {
                publicKey = this.publicKey,
                storedKey = this.storedKey,
                expiration = this.expiration,
                Iv = this.Iv,
                KeyHash = this.KeyHash,
                crc32c = this.crc32c
            };
        }
    }
}