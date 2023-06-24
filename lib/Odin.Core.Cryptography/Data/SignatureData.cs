using Odin.Core.Cryptography.Crypto;
using Odin.Core.Identity;
using Odin.Core.Time;
using System.Text.Json.Serialization;
using System;

namespace Odin.Core.Cryptography.Data
{
    public class SignatureData
    {
        [JsonPropertyOrder(1)]
        public const int Version = 1;

        [JsonPropertyOrder(2)]
        public byte[] DataHash { get; set; }

        [JsonPropertyOrder(3)]
        public string DataHashAlgorithm { get; set; }

        [JsonPropertyOrder(4)]
        public OdinId Identity { get; set; }

        [JsonPropertyOrder(5)]
        public byte[] PublicKeyDer { get; set; }

        [JsonPropertyOrder(6)]
        public UnixTimeUtc TimeStamp { get; set; }

        [JsonPropertyOrder(7)]
        public string SignatureAlgorithm { get; set; }

        [JsonPropertyOrder(8)]
        public byte[] DocumentSignature { get; set; }

        public SignatureData()
        {
            // Default constructor 
        }

        /// <summary>
        /// Signs a generic set of data and creates a signature
        /// </summary>
        /// <param name="data"></param>
        /// <param name="identity"></param>
        /// <param name="keyPwd"></param>
        /// <param name="eccKey"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static SignatureData Sign(byte[] data, OdinId identity, SensitiveByteArray keyPwd, EccFullKeyData eccKey)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentNullException(nameof(data));
            if (keyPwd == null)
                throw new ArgumentNullException(nameof(keyPwd));
            if (eccKey == null)
                throw new ArgumentNullException(nameof(eccKey));

            var s = new SignatureData();

            s.DataHash = ByteArrayUtil.CalculateSHA256Hash(data);
            s.DataHashAlgorithm = HashUtil.SHA256Algorithm;
            s.Identity = identity;
            s.PublicKeyDer = eccKey.publicKey;
            s.TimeStamp = UnixTimeUtc.Now();
            s.SignatureAlgorithm = EccFullKeyData.eccSignatureAlgorithm;
            var bytesToSign = ByteArrayUtil.Combine(s.DataHash, s.DataHashAlgorithm.ToUtf8ByteArray(), s.Identity.ToByteArray(), s.PublicKeyDer, ByteArrayUtil.Int64ToBytes(s.TimeStamp.milliseconds), s.SignatureAlgorithm.ToUtf8ByteArray());

            s.DocumentSignature = eccKey.Sign(keyPwd, bytesToSign);

            return s;
        }

        public static bool Verify(SignatureData signatureData, byte[] dataOriginallySigned)
        {
            if (signatureData == null)
                throw new ArgumentNullException(nameof(signatureData));

            var dataHash = ByteArrayUtil.CalculateSHA256Hash(dataOriginallySigned);
            
            // Verify that the original data hash is the same as in the signature
            if (ByteArrayUtil.EquiByteArrayCompare(dataHash, signatureData.DataHash) == false)
                return false;

            // It's the same hash, validate the signature
            var publicKey = EccPublicKeyData.FromDerEncodedPublicKey(signatureData.PublicKeyDer);
            var bytesToSign = ByteArrayUtil.Combine(
                                    signatureData.DataHash, 
                                    signatureData.DataHashAlgorithm.ToUtf8ByteArray(),
                                    signatureData.Identity.ToByteArray(),
                                    signatureData.PublicKeyDer, 
                                    ByteArrayUtil.Int64ToBytes(signatureData.TimeStamp.milliseconds), 
                                    signatureData.SignatureAlgorithm.ToUtf8ByteArray());

            return publicKey.VerifySignature(bytesToSign, signatureData.DocumentSignature);
        }

        public string GetCompactSortedJson()
        {
            return System.Text.Json.JsonSerializer.Serialize(this);
        }
    }
}
