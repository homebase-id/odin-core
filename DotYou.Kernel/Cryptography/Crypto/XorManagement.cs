using DotYou.AdminClient.Extensions;
using System;
using System.Security.Cryptography;
using System.Text;

namespace DotYou.Kernel.Cryptography
{
    public static class XorManagement
    {
        /// <summary>
        /// Returns the XOR'ed key which you can e.g. safely store in the DB.
        /// </summary>
        /// <param name="data">Typically the client token. Shouldn't be on the server.</param>
        /// <param name="key">The secret key to protect.</param>
        /// <returns>The XOR'ed key</returns>
        public static byte[] XorEncrypt(byte[] data, byte[] key)
        {
            return YFByteArray.EquiByteArrayXor(data, key);
        }

        /// <summary>
        /// Takes the XOR'ed key and returns the key
        /// </summary>
        /// <param name="ciper">Typically the client token. Shouldn't be on the server.</param>
        /// <param name="key">The XOR'ed key</param>
        /// <returns>The secret key</returns>
        public static byte[] XorDecrypt(byte[] ciper, byte[] key)
        {
            return YFByteArray.EquiByteArrayXor(key, ciper);
        }



        /// <summary>
        /// Takes the XOR'ed key, an old token and a new token and returns the new xorKey
        /// </summary>
        /// <param name="oldToken">Typically the client token. Shouldn't be on the server. The token to replace</param>
        /// <param name="newToken">The new token to replace it with</param>
        /// <param name="xorKey">The new Xor key</param>
        /// <returns></returns>
        public static byte[] RefreshToken(byte[] oldToken, byte[] newToken, byte[] xorKey)
        {
            var key = XorManagement.XorDecrypt(oldToken, xorKey);
            var newXorKey = XorManagement.XorEncrypt(newToken, key);
            YFByteArray.WipeByteArray(key);

            return newXorKey;
        }

        /// <summary>
        /// Given any secret 'key' split it into a random value and the cipher.
        /// </summary>
        /// <param name="key">The key to split into two halves</param>
        /// <returns>The cipher and the random key needed to decrypt the cipher</returns>
        public static (byte[] cipher, byte[] random) XorSplitKey(byte[] key)
        {
            var rndHalf = YFByteArray.GetRndByteArray(key.Length);
            var cipher = XorManagement.XorEncrypt(key, rndHalf);

            return (cipher, rndHalf);
        }
    }

}
