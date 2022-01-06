using System.IO;
using Newtonsoft.Json;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Hosting.Tests.AppAPI
{
    public static class Utils
    {
        public static Stream GetEncryptedStream(string data, KeyHeader keyHeader)
        {
            var cipher = Core.Cryptography.Crypto.AesCbc.EncryptBytesToBytes_Aes(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: keyHeader.AesKey.GetKey(),
                iv: keyHeader.Iv);

            return new MemoryStream(cipher);
        }

        public static Stream EncryptAes(string data, byte[] iv, byte[] key)
        {
            var cipher = Core.Cryptography.Crypto.AesCbc.EncryptBytesToBytes_Aes(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: key,
                iv: iv);

            return new MemoryStream(cipher);
        }
        
        /// <summary>
        /// Converts data to json then encrypts
        /// </summary>
        public static Stream JsonEncryptAes(object instance, byte[] iv, byte[] key)
        {
            var data = JsonConvert.SerializeObject(instance);
            
            var cipher = Core.Cryptography.Crypto.AesCbc.EncryptBytesToBytes_Aes(
                data: System.Text.Encoding.UTF8.GetBytes(data),
                key: key,
                iv: iv);

            return new MemoryStream(cipher);
        }
    }
}