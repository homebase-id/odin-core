using Youverse.Core.Cryptography;

namespace Youverse.Core.Services.Transit.Encryption
{
    public class KeyHeader
    {
        public byte[] Iv { get; set; }

        public SecureKey AesKey { get; set; }

        public SecureKey Combine()
        {
            return new SecureKey(ByteArrayUtil.Combine(this.Iv, this.AesKey.GetKey()));
        }

        public static KeyHeader FromCombinedBytes(byte[] data, int ivLength, int keyLength)
        {
            var (p1, p2) = ByteArrayUtil.Split(data, ivLength, keyLength);
            return new KeyHeader()
            {
                Iv = p1,
                AesKey = new SecureKey(p2)
            };
        }
    }
}