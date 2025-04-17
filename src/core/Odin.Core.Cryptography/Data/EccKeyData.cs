using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
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
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Odin.Core.Cryptography.Data
{
    public enum EccKeySize
    {
        P256 = 0,
        P384 = 1
    }

    public class EccPublicKeyData
    {
        public static string[] eccSignatureAlgorithmNames = new string[2] { "SHA-256withECDSA", "SHA-384withECDSA" };
        public static string[] eccKeyTypeNames = new string[2] { "P-256", "P-384" };
        public static string[] eccCurveIdentifiers = new string[2] { "secp256r1", "secp384r1" };

        public byte[] publicKey { get; set; } // DER encoded public key

        public UInt32 crc32c { get; set; } // The CRC32C of the public key
        public UnixTimeUtc expiration { get; set; } // Time when this key expires

        public static EccPublicKeyData FromJwkPublicKey(string jwk, int hours = 1)
        {
            try
            {
                var jwkObject = JsonSerializer.Deserialize<Dictionary<string, string>>(jwk);

                if (jwkObject["kty"] != "EC")
                    throw new InvalidOperationException("Invalid key type, kty must be EC");

                string curveName = jwkObject["crv"];
                if ((curveName != "P-384") && (curveName != "P-256"))
                    throw new InvalidOperationException("Invalid curve, crv must be P-384 OR P-256");

                byte[] x = Base64UrlEncoder.Decode(jwkObject["x"]);
                byte[] y = Base64UrlEncoder.Decode(jwkObject["y"]);

                X9ECParameters x9ECParameters = NistNamedCurves.GetByName(curveName);
                ECCurve curve = x9ECParameters.Curve;
                ECPoint ecPoint = curve.CreatePoint(new BigInteger(1, x), new BigInteger(1, y));

                ECPublicKeyParameters publicKeyParameters = new ECPublicKeyParameters(ecPoint,
                    new ECDomainParameters(curve, x9ECParameters.G, x9ECParameters.N, x9ECParameters.H));

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
            catch (FormatException)
            {
                throw new OdinClientException("Invalid Jwk public key format");
            }
        }

        public static EccPublicKeyData FromJwkBase64UrlPublicKey(string jwkbase64Url, int hours = 1)
        {
            return FromJwkPublicKey(Base64UrlEncoder.DecodeString(jwkbase64Url), hours);
        }

        protected EccKeySize GetCurveEnum(ECCurve curve)
        {
            int bitLength = curve.Order.BitLength;

            if (bitLength == 384)
            {
                return EccKeySize.P384;
            }
            else if (bitLength == 256)
            {
                return EccKeySize.P256;
            }
            else
            {
                throw new Exception($"Unsupported ECC key size with bit length: {bitLength}");
            }
        }


        // Method to ensure byte array length
        private byte[] EnsureLength(byte[] bytes, int length)
        {
            if (bytes.Length >= length) return bytes;

            byte[] paddedBytes = new byte[length];
            Array.Copy(bytes, 0, paddedBytes, length - bytes.Length, bytes.Length);
            return paddedBytes;
        }


        public string PublicKeyJwk()
        {
            var publicKeyRestored = PublicKeyFactory.CreateKey(publicKey);
            ECPublicKeyParameters publicKeyParameters = (ECPublicKeyParameters)publicKeyRestored;

            // Extract the key parameters
            BigInteger x = publicKeyParameters.Q.AffineXCoord.ToBigInteger();
            BigInteger y = publicKeyParameters.Q.AffineYCoord.ToBigInteger();

            var curveSize = GetCurveEnum((ECCurve)publicKeyParameters.Parameters.Curve);

            int expectedBytes;
            if (curveSize == EccKeySize.P384)
                expectedBytes = 384 / 8;
            else
                expectedBytes = 256 / 8;

            var xBytes = EnsureLength(x.ToByteArrayUnsigned(), expectedBytes);
            var yBytes = EnsureLength(y.ToByteArrayUnsigned(), expectedBytes);

            string curveName = eccKeyTypeNames[(int)curveSize];

            // Create a JSON object to represent the JWK
            var jwk = new
            {
                kty = "EC",
                crv = curveName, // P-256 or P-384
                x = Base64UrlEncoder.Encode(xBytes),
                y = Base64UrlEncoder.Encode(yBytes)
            };

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };

            string jwkJson = JsonSerializer.Serialize(jwk, options);

            return jwkJson;
        }

        public string GenerateEcdsaBase64Url()
        {
            var publicKeyRestored = PublicKeyFactory.CreateKey(publicKey);
            ECPublicKeyParameters publicKeyParameters = (ECPublicKeyParameters)publicKeyRestored;

            // Extract X and Y coordinates
            byte[] x = publicKeyParameters.Q.AffineXCoord.GetEncoded();
            byte[] y = publicKeyParameters.Q.AffineYCoord.GetEncoded();

            // Uncompressed key format: 0x04 | X | Y
            byte[] uncompressedKey = new byte[1 + x.Length + y.Length];
            uncompressedKey[0] = 0x04;
            Buffer.BlockCopy(x, 0, uncompressedKey, 1, x.Length);
            Buffer.BlockCopy(y, 0, uncompressedKey, 1 + x.Length, y.Length);

            // Encode to URL-safe Base64 without padding
            return Base64UrlEncoder.Encode(uncompressedKey);
        }

        public string PublicKeyJwkBase64Url()
        {
            return Base64UrlEncoder.Encode(PublicKeyJwk());
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
            ECPublicKeyParameters publicKeyParameters = (ECPublicKeyParameters)publicKeyRestored;

            ISigner signer =
                SignerUtilities.GetSigner(eccSignatureAlgorithmNames[(int)GetCurveEnum((ECCurve)publicKeyParameters.Parameters.Curve)]);

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
        private SensitiveByteArray _privateKey; // Cached decrypted private key, not stored

        public byte[] storedKey { get; set; } // The key as stored on disk encrypted with a secret key or constant

        public byte[] iv { get; set; } // Iv used for encrypting the storedKey and the masterCopy
        public byte[] keyHash { get; set; } // The hash of the encryption key

        public UnixTimeUtc
            createdTimeStamp
        {
            get;
            set;
        } // Time when this key was created, expiration is on the public key. Do NOT use a property or code will return a copy value.


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
        /// <param name="size"></param>
        /// <param name="hours">Lifespan of the key, required</param>
        /// <param name="minutes">Lifespan of the key, optional</param>
        /// <param name="seconds">Lifespan of the key, optional</param>
        public EccFullKeyData(SensitiveByteArray key, EccKeySize keySize, int hours, int minutes = 0, int seconds = 0)
        {
            // Generate an EC key with Bouncy Castle, curve secp384r1
            ECKeyPairGenerator generator = new ECKeyPairGenerator();
            X9ECParameters ecp = SecNamedCurves.GetByName(eccCurveIdentifiers[(int)keySize]);

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

            CreatePrivate(key, privateKeyInfo.GetDerEncoded()); // TODO: Can we cleanup the generated key?

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
            Interlocked.Increment(ref SimplePerformanceCounter.noRsaKeysCreatedTest);
        }


        private void CreatePrivate(SensitiveByteArray key, byte[] fullDerKey)
        {
            iv = ByteArrayUtil.GetRndByteArray(16);
            keyHash = ByteArrayUtil.ReduceSHA256Hash(key.GetKey());
            _privateKey = new SensitiveByteArray(fullDerKey);
            storedKey = AesCbc.Encrypt(_privateKey.GetKey(), key, iv);
        }


        private SensitiveByteArray GetFullKey(SensitiveByteArray key)
        {
            if (ByteArrayUtil.EquiByteArrayCompare(keyHash, ByteArrayUtil.ReduceSHA256Hash(key.GetKey())) == false)
                throw new Exception("Incorrect key");

            if (_privateKey == null)
            {
                _privateKey = new SensitiveByteArray(AesCbc.Decrypt(storedKey, key, iv));
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

        public byte[] Sign(SensitiveByteArray key, byte[] dataToSign)
        {
            var pk = GetFullKey(key);

            var publicKeyRestored = PublicKeyFactory.CreateKey(publicKey);
            ECPublicKeyParameters publicKeyParameters = (ECPublicKeyParameters)publicKeyRestored;

            var privateKeyRestored = PrivateKeyFactory.CreateKey(pk.GetKey());

            ISigner signer =
                SignerUtilities.GetSigner(eccSignatureAlgorithmNames[(int)GetCurveEnum((ECCurve)publicKeyParameters.Parameters.Curve)]);

            signer.Init(true, privateKeyRestored); // Init for signing (true), with the private key

            signer.BlockUpdate(dataToSign, 0, dataToSign.Length);

            byte[] signature = signer.GenerateSignature();

            return signature;
        }
    }
}