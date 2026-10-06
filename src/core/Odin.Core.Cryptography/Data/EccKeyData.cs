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
using Org.BouncyCastle.Utilities;
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

        protected static readonly JsonSerializerOptions JwkJsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// The members every EC JWK has, checked and decoded; the private import adds <c>d</c>.
        /// </summary>
        protected static (EccKeySize size, byte[] x, byte[] y, Dictionary<string, string> members) ReadEcJwk(string jwk)
        {
            var jwkObject = JsonSerializer.Deserialize<Dictionary<string, string>>(jwk);

            if (jwkObject["kty"] != "EC")
                throw new InvalidOperationException("Invalid key type, kty must be EC");

            var curveIndex = Array.IndexOf(eccKeyTypeNames, jwkObject["crv"]);
            if (curveIndex < 0)
                throw new InvalidOperationException("Invalid curve, crv must be P-384 OR P-256");

            byte[] x = Base64UrlEncoder.Decode(jwkObject["x"]);
            byte[] y = Base64UrlEncoder.Decode(jwkObject["y"]);

            return ((EccKeySize)curveIndex, x, y, jwkObject);
        }

        public static EccPublicKeyData FromJwkPublicKey(string jwk, int hours = 1)
        {
            try
            {
                var (curveSize, x, y, _) = ReadEcJwk(jwk);
                string curveName = eccKeyTypeNames[(int)curveSize];

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
            // Malformed base64 throws FormatException; BouncyCastle rejects a coordinate outside the field or a
            // point off the curve with ArgumentException. Either way the JWK is invalid input (#1812).
            catch (Exception e) when (e is FormatException or ArgumentException)
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


        // Left-pads key material that lost its leading zero bytes. Longer than the field is not a short encoding
        // but a wrong key, curve or parse, and passing it on yields a silently wrong key -- so it is rejected (#1812).
        protected static byte[] EnsureLength(byte[] bytes, int length)
        {
            if (bytes.Length > length)
                throw new OdinSystemException($"Key material is {bytes.Length} bytes, expected at most {length}");
            if (bytes.Length == length) return bytes;

            byte[] paddedBytes = new byte[length];
            Array.Copy(bytes, 0, paddedBytes, length - bytes.Length, bytes.Length);
            return paddedBytes;
        }


        protected static int CoordinateBytes(EccKeySize size) => size == EccKeySize.P384 ? 384 / 8 : 256 / 8;

        /// <summary>The public JWK's members: the curve and the coordinates, zero-padded to the curve size, base64url.</summary>
        protected (EccKeySize size, string crv, string x, string y) PublicJwkMembers()
        {
            var publicKeyParameters = (ECPublicKeyParameters)PublicKeyFactory.CreateKey(publicKey);
            var curveSize = GetCurveEnum((ECCurve)publicKeyParameters.Parameters.Curve);
            var bytes = CoordinateBytes(curveSize);

            return (
                curveSize,
                eccKeyTypeNames[(int)curveSize],
                Base64UrlEncoder.Encode(BigIntegers.AsUnsignedByteArray(bytes, publicKeyParameters.Q.AffineXCoord.ToBigInteger())),
                Base64UrlEncoder.Encode(BigIntegers.AsUnsignedByteArray(bytes, publicKeyParameters.Q.AffineYCoord.ToBigInteger())));
        }

        public string PublicKeyJwk()
        {
            var (_, crv, x, y) = PublicJwkMembers();
            return JsonSerializer.Serialize(new { kty = "EC", crv, x, y }, JwkJsonOptions);
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
            : this(key, Generate(keySize), hours, minutes, seconds)
        {
            EccKeyManagement.noKeysCreated++;
        }

        /// <summary>
        /// The key from its two halves, held under <paramref name="key"/> for the given lifetime:
        /// what the generating constructor and the JWK import have in common.
        /// </summary>
        private EccFullKeyData(SensitiveByteArray key, AsymmetricCipherKeyPair keys, int hours, int minutes, int seconds)
        {
            createdTimeStamp = UnixTimeUtc.Now();
            expiration = createdTimeStamp.AddSeconds(hours * 3600 + minutes * 60 + seconds);
            if (expiration <= createdTimeStamp)
                throw new Exception("Expiration must be > 0");

            CreatePrivate(key, PrivateKeyInfoFactory.CreatePrivateKeyInfo(keys.Private).GetDerEncoded()); // TODO: Can we cleanup the generated key?

            publicKey = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keys.Public).GetDerEncoded();
            crc32c = KeyCRC();
        }

        /// <summary>
        /// The curve as this class encodes it, seed included: what every full key's DER, and so its
        /// CRC, is written against, whether generated here or rebuilt from a JWK.
        /// </summary>
        private static ECDomainParameters DomainParameters(EccKeySize keySize)
        {
            X9ECParameters ecp = SecNamedCurves.GetByName(eccCurveIdentifiers[(int)keySize]);
            return new ECDomainParameters(ecp.Curve, ecp.G, ecp.N, ecp.H, ecp.GetSeed());
        }

        private static AsymmetricCipherKeyPair Generate(EccKeySize keySize)
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(DomainParameters(keySize), new SecureRandom()));
            return generator.GenerateKeyPair();
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

        /// <summary>
        /// The key as a private JWK (RFC 7518 section 6.2.2): the public key's members plus <c>d</c>,
        /// the private scalar, base64url and zero-padded to the curve size like the coordinates.
        /// Small and portable, where the DER form spells the curve parameters out.
        /// </summary>
        public string PrivateKeyJwk(SensitiveByteArray key)
        {
            var (curveSize, crv, x, y) = PublicJwkMembers();
            var privateKeyParameters = (ECPrivateKeyParameters)PrivateKeyFactory.CreateKey(GetFullKey(key).GetKey());
            var d = Base64UrlEncoder.Encode(BigIntegers.AsUnsignedByteArray(CoordinateBytes(curveSize), privateKeyParameters.D));

            return JsonSerializer.Serialize(new { kty = "EC", crv, x, y, d }, JwkJsonOptions);
        }

        public string PrivateKeyJwkBase64Url(SensitiveByteArray key)
        {
            return Base64UrlEncoder.Encode(PrivateKeyJwk(key));
        }

        /// <summary>
        /// A key from its private JWK, held under <paramref name="key"/> like a generated one; the
        /// public half is rebuilt from <c>x</c> and <c>y</c>. The lifetime is the caller's, as for
        /// <see cref="EccPublicKeyData.FromJwkPublicKey"/>: a JWK carries none.
        /// </summary>
        public static EccFullKeyData FromJwkPrivateKey(SensitiveByteArray key, string jwk, int hours = 1)
        {
            try
            {
                var (curveSize, x, y, members) = ReadEcJwk(jwk);
                if (!members.TryGetValue("d", out var dText))
                    throw new InvalidOperationException("Not a private key, d is missing");
                byte[] d = Base64UrlEncoder.Decode(dText);

                var domainParams = DomainParameters(curveSize);
                var keys = new AsymmetricCipherKeyPair(
                    new ECPublicKeyParameters(domainParams.Curve.CreatePoint(new BigInteger(1, x), new BigInteger(1, y)), domainParams),
                    new ECPrivateKeyParameters(new BigInteger(1, d), domainParams));

                return new EccFullKeyData(key, keys, hours, 0, 0);
            }
            catch (Exception e) when (e is FormatException or ArgumentException) // see FromJwkPublicKey
            {
                throw new OdinClientException("Invalid Jwk private key format");
            }
        }

        public static EccFullKeyData FromJwkBase64UrlPrivateKey(SensitiveByteArray key, string jwkBase64Url, int hours = 1)
        {
            return FromJwkPrivateKey(key, Base64UrlEncoder.DecodeString(jwkBase64Url), hours);
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

            // Calculate the shared secret: the X coordinate of the shared point, encoded at the curve's field
            // length and zero-padded, as WebCrypto, JCA and the other platform ECDH implementations do. Encoding it
            // with ToByteArrayUnsigned() instead dropped a leading zero byte, so about 1 exchange in 256 derived a
            // different key here than on the client (#1728).
            var sharedSecret = ecdhUagree.CalculateAgreement(publicKeyParameters);
            var sharedSecretBytes = EnsureLength(sharedSecret.ToByteArrayUnsigned(), ecdhUagree.GetFieldSize());

            // Apply HKDF to derive a symmetric key from the shared secret
            return HashUtil.Hkdf(sharedSecretBytes, randomSalt, 16).ToSensitiveByteArray();
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