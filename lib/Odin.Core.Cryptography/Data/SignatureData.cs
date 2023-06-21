using Odin.Core.Cryptography.Crypto;
using Odin.Core.Identity;
using Odin.Core.Time;
using System;

namespace Odin.Core.Cryptography.Data
{
    public class SignatureData
    {
        public byte[] DataHash { get; set; }
        public string DataHashAlgorithm { get; set; }
        public OdinId Identity { get; set; }
        public byte[] PublicKeyDer { get; set; }
        public UnixTimeUtc TimeStamp { get; set; }
        public string SignatureAlgorithm { get; set; }
        public byte[] DocumentSignature { get; set; }

        public SignatureData()
        {
            // Default constructor 
        }

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

        public static bool Verify(SignatureData signatureData)
        {
            if (signatureData == null)
                throw new ArgumentNullException(nameof(signatureData));

            var publicKey = EccPublicKeyData.FromDerEncodedPublicKey(signatureData.PublicKeyDer);

            var bytesToSign = ByteArrayUtil.Combine(signatureData.DataHash, signatureData.DataHashAlgorithm.ToUtf8ByteArray(), signatureData.Identity.ToByteArray(), signatureData.PublicKeyDer, ByteArrayUtil.Int64ToBytes(signatureData.TimeStamp.milliseconds), signatureData.SignatureAlgorithm.ToUtf8ByteArray());

            return publicKey.VerifySignature(bytesToSign, signatureData.DocumentSignature);
        }
    }
}
