using System.IO;
using Youverse.Core.Cryptography;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Hosting.Tests.AppAPI
{
    public static class Utilsx
    {
        public static Stream GetEncryptedStream(string data, KeyHeader keyHeader)
        {
            var key = keyHeader.AesKey;
            var cipher = Core.Cryptography.Crypto.AesCbc.Encrypt(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: ref key,
                iv: keyHeader.Iv);

            return new MemoryStream(cipher);
        }

        public static Stream EncryptAes(string data, byte[] iv, ref SensitiveByteArray key)
        {
            var cipher = Core.Cryptography.Crypto.AesCbc.Encrypt(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: ref key,
                iv: iv);

            return new MemoryStream(cipher);
        }
        
        /// <summary>
        /// Converts data to json then encrypts
        /// </summary>
        public static Stream JsonEncryptAes(object instance, byte[] iv, ref SensitiveByteArray key)
        {
            var data = DotYouSystemSerializer.Serialize(instance);
            
            var cipher = Core.Cryptography.Crypto.AesCbc.Encrypt(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: ref key,
                iv: iv);

            return new MemoryStream(cipher);
        }
    }
}