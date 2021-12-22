using System.IO;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Hosting.Tests
{
    public static class UploadEncryptionUtils
    {
        public static Stream GetEncryptedStream(string data, KeyHeader keyHeader)
        {
            var cipher = Core.Cryptography.Crypto.AesCbc.EncryptBytesToBytes_Aes(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: keyHeader.AesKey.GetKey(),
                iv: keyHeader.Iv);

            return new MemoryStream(cipher);
        }

        public static Stream GetAppSharedSecretEncryptedStream(string data, byte[] iv, byte[] key)
        {
            var cipher = Core.Cryptography.Crypto.AesCbc.EncryptBytesToBytes_Aes(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: key,
                iv: iv);

            return new MemoryStream(cipher);
        }
    }
}