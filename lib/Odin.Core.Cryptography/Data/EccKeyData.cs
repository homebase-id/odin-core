using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Time;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Odin.Core.Cryptography.Data
{
    public class EccPublicKeyData
    {
        public byte[] publicKey { get; set; } // DER encoded public key

        public UInt32 crc32c { get; set; } // The CRC32C of the public key
        public UnixTimeUtc expiration { get; set; } // Time when this key expires


        //
        // Perhaps move to other lib/ class
        // 

        public static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input).Split('=')[0].Replace('+', '-').Replace('/', '_');
        }
        public static string Base64UrlEncode(string input)
        {
            return Convert.ToBase64String(input.ToUtf8ByteArray()).Split('=')[0].Replace('+', '-').Replace('/', '_');
        }

        public static byte[] Base64UrlDecode(string input)
        {
            string base64 = input.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }

        public static string Base64UrlDecodeString(string input)
        {
            return Base64UrlDecode(input).ToStringFromUtf8Bytes();
        }


        //
        // End of stuff to be put in another class
        //
        public static EccPublicKeyData FromJwkPublicKey(string jwk, int hours = 1)
        {
            var jwkObject = JsonSerializer.Deserialize<Dictionary<string, string>>(jwk);

            if (jwkObject["kty"] != "EC")
                throw new InvalidOperationException("Invalid key type, kty must be EC");

            if (jwkObject["crv"] != "P-384")
                throw new InvalidOperationException("Invalid curve, crv must be P-384");

            byte[] x = Base64UrlDecode(jwkObject["x"]);
            byte[] y = Base64UrlDecode(jwkObject["y"]);

            X9ECParameters x9ECParameters = NistNamedCurves.GetByName("P-384");
            ECCurve curve = x9ECParameters.Curve;
            ECPoint ecPoint = curve.CreatePoint(new BigInteger(1, x), new BigInteger(1, y));

            ECPublicKeyParameters publicKeyParameters = new ECPublicKeyParameters(ecPoint, new ECDomainParameters(curve, x9ECParameters.G, x9ECParameters.N, x9ECParameters.H));

            SubjectPublicKeyInfo publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKeyParameters);
            byte[] derEncodedPublicKey = publicKeyInfo.GetDerEncoded();

            var publicKey = new EccPublicKeyData()
            {
                publicKey = derEncodedPublicKey,
                crc32c = KeyCRC(derEncodedPublicKey),
                expiration = UnixTimeUtc.Now().AddSeconds(hours * 60 * 60)
            };

            return publicKey;
        }

        public static EccPublicKeyData FromJwkBase64UrlPublicKey(string jwkbase64Url, int hours = 1)
        {
            return FromJwkPublicKey(Base64UrlDecodeString(jwkbase64Url) , hours);
        }

        public string PublicKeyJwk()
        {
            var publicKeyRestored = PublicKeyFactory.CreateKey(publicKey);
            ECPublicKeyParameters publicKeyParameters = (ECPublicKeyParameters)publicKeyRestored;

            // Extract the key parameters
            BigInteger x = publicKeyParameters.Q.AffineXCoord.ToBigInteger();
            BigInteger y = publicKeyParameters.Q.AffineYCoord.ToBigInteger();

            // Create a JSON object to represent the JWK
            var jwk = new
            {
                kty = "EC",
                crv = "P-384", // Corresponds to "secp384r1"
                x = Base64UrlEncode(x.ToByteArrayUnsigned()),
                y = Base64UrlEncode(y.ToByteArrayUnsigned())
            };

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };

            string jwkJson = JsonSerializer.Serialize(jwk, options);

            return jwkJson;
        }

        public string PublicKeyJwkBase64Url()
        {
            return Base64UrlEncode(PublicKeyJwk());
        }


        public static UInt32 KeyCRC(byte[] keyDerEncoded)
        {
            return CRC32C.CalculateCRC32C(0, keyDerEncoded);
        }

        public UInt32 KeyCRC()
        {
            return KeyCRC(publicKey);
        }

        public bool VerifySignature(byte[] dataThatWasSigned, byte[] signature)
        {
            var publicKeyRestored = PublicKeyFactory.CreateKey(publicKey);

            ISigner signer = SignerUtilities.GetSigner(EccFullKeyData.eccSignatureAlgorithm);
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

        public bool IsValid()
        {
            return !IsExpired();
        }
    }

    public class EccFullKeyData : EccPublicKeyData
    {
        public static string eccSignatureAlgorithm = "SHA-384withECDSA";
        private SensitiveByteArray _privateKey;  // Cached decrypted private key, not stored

        public byte[] storedKey { get; set; }  // The key as stored on disk encrypted with a secret key or constant

        public byte[] iv { get; set; }  // Iv used for encrypting the storedKey and the masterCopy
        public byte[] keyHash { get; set; }  // The hash of the encryption key 
        public UnixTimeUtc createdTimeStamp { get; set; } // Time when this key was created, expiration is on the public key. Do NOT use a property or code will return a copy value.


        /// <summary>
        /// For LiteDB read only.
        /// </summary>
        public EccFullKeyData()
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
        public EccFullKeyData(SensitiveByteArray key, int hours, int minutes = 0, int seconds = 0)
        {
            // Generate an EC key with Bouncy Castle, curve secp384r1
            ECKeyPairGenerator generator = new ECKeyPairGenerator();
            var ecp = SecNamedCurves.GetByName("secp384r1");
            var domainParams = new ECDomainParameters(ecp.Curve, ecp.G, ecp.N, ecp.H, ecp.GetSeed());
            generator.Init(new ECKeyGenerationParameters(domainParams, new SecureRandom()));
            AsymmetricCipherKeyPair keys = generator.GenerateKeyPair();

            // Extract the public and the private keys
            var privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keys.Private);
            var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keys.Public);

            // Save the DER encoded private and public keys in our own data structure
            createdTimeStamp = UnixTimeUtc.Now();
            expiration = createdTimeStamp;
            expiration = expiration.AddSeconds(hours * 3600 + minutes * 60 + seconds);
            if (expiration <= createdTimeStamp)
                throw new Exception("Expiration must be > 0");

            CreatePrivate(key, privateKeyInfo.GetDerEncoded());  // TODO: Can we cleanup the generated key?

            publicKey = publicKeyInfo.GetDerEncoded();
            crc32c = KeyCRC();

            EccKeyManagement.noKeysCreated++;
        }

        /// <summary>
        /// Hack used only for TESTING.
        /// </summary>
        public EccFullKeyData(SensitiveByteArray key, byte[] derEncodedFullKey)
        {
            // ONLY USE FOR TESTING. DOES NOT CREATE PUBLIC KEY PROPERLY
            CreatePrivate(key, derEncodedFullKey);

            //_privateKey = new SensitiveByteArray(derEncodedFullKey);
            // createdTimeStamp = DateTimeExtensions.UnixTimeSeconds();
            //var pkRestored = PublicKeyFactory.CreateKey(derEncodedFulKey);
            //var pk = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pkRestored);
            //publicKey = pk.GetDerEncoded();
            RsaKeyManagement.noKeysCreatedTest++;
        }



        private void CreatePrivate(SensitiveByteArray key, byte[] fullDerKey)
        {
            iv = ByteArrayUtil.GetRndByteArray(16);
            keyHash = ByteArrayUtil.ReduceSHA256Hash(key.GetKey());
            _privateKey = new SensitiveByteArray(fullDerKey);
            storedKey = AesCbc.Encrypt(_privateKey.GetKey(), ref key, iv);
        }


        private SensitiveByteArray GetFullKey(SensitiveByteArray key)
        {
            if (ByteArrayUtil.EquiByteArrayCompare(keyHash, ByteArrayUtil.ReduceSHA256Hash(key.GetKey())) == false)
                throw new Exception("Incorrect key");

            if (_privateKey == null)
            {
                _privateKey = new SensitiveByteArray(AesCbc.Decrypt(storedKey, ref key, iv));
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


        public SensitiveByteArray GetEcdhSharedSecret(SensitiveByteArray pwd, EccPublicKeyData remotePublicKey, byte[] randomSalt)
        {
            if (remotePublicKey == null)
                throw new ArgumentNullException(nameof(remotePublicKey));

            if (remotePublicKey.publicKey == null)
                throw new ArgumentNullException(nameof(remotePublicKey.publicKey));

            if (randomSalt == null)
                throw new ArgumentNullException(nameof(randomSalt));

            if (randomSalt.Length < 16)
                throw new ArgumentException("Salt must be at least 16 bytes");

            // Retrieve the private key from the secure storage
            var privateKeyBytes = GetFullKey(pwd).GetKey();
            var privateKeyParameters = (ECPrivateKeyParameters)PrivateKeyFactory.CreateKey(privateKeyBytes);

            // Construct the public key parameters from the provided data
            var publicKeyParameters = (ECPublicKeyParameters)PublicKeyFactory.CreateKey(remotePublicKey.publicKey);

            // Initialize ECDH basic agreement
            ECDHBasicAgreement ecdhUagree = new ECDHBasicAgreement();
            ecdhUagree.Init(privateKeyParameters);

            // Calculate the shared secret
            BigInteger sharedSecret = ecdhUagree.CalculateAgreement(publicKeyParameters);

            // Convert the shared secret to a byte array
            var sharedSecretBytes = sharedSecret.ToByteArrayUnsigned().ToSensitiveByteArray();

            // Apply HKDF to derive a symmetric key from the shared secret
            return HashUtil.Hkdf(sharedSecretBytes.GetKey(), randomSalt, 16).ToSensitiveByteArray();
        }

        [Obsolete("Use GetEcdhSharedSecret() instead and always use a random salt. Send the random salt over the wire.")]
        public (byte[] tokenToTransmit, SensitiveByteArray SharedSecret) NewTransmittableSharedSecret(SensitiveByteArray pwd, EccPublicKeyData remotePublicKey, byte[] salt)
        {
            var ecdhSS = GetEcdhSharedSecret(pwd, remotePublicKey, salt);

            return (salt, ecdhSS);
        }

        [Obsolete("Use GetEcdhSharedSecret() instead and always use a random salt. Send the random salt over the wire.")]
        public SensitiveByteArray ResolveSharedSecret(SensitiveByteArray pwd, byte[] tokenReceived, EccPublicKeyData remotePublicKey, byte[] salt)
        {
            var ecdhSS = GetEcdhSharedSecret(pwd, remotePublicKey, salt);

            return ecdhSS;
        }

        public byte[] Sign(SensitiveByteArray key, byte[] dataToSign)
        {
            var pk = GetFullKey(key);

            var privateKeyRestored = PrivateKeyFactory.CreateKey(pk.GetKey());

            // Assuming that 'keys' is your AsymmetricCipherKeyPair
            ISigner signer = SignerUtilities.GetSigner(eccSignatureAlgorithm);
            signer.Init(true, privateKeyRestored); // Init for signing (true), with the private key

            signer.BlockUpdate(dataToSign, 0, dataToSign.Length);

            byte[] signature = signer.GenerateSignature();

            return signature;
        }
    }
}
