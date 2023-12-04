using System.IO;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.AppAPI.Utils
{
    public static class TestUtils
    {
        public static Stream GetEncryptedStream(string data, KeyHeader keyHeader)
        {
            var key = keyHeader.AesKey;
            var cipher = AesCbc.Encrypt(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: key,
                iv: keyHeader.Iv);

            return new MemoryStream(cipher);
        }

        public static Stream EncryptAes(string data, byte[] iv, ref SensitiveByteArray key)
        {
            var cipher = AesCbc.Encrypt(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: key,
                iv: iv);

            return new MemoryStream(cipher);
        }
        
        /// <summary>
        /// Converts data to json then encrypts
        /// </summary>
        public static Stream JsonEncryptAes(object instance, byte[] iv, ref SensitiveByteArray key)
        {
            var data = OdinSystemSerializer.Serialize(instance);
            
            var cipher = AesCbc.Encrypt(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: key,
                iv: iv);

            return new MemoryStream(cipher);
        }
    }
}