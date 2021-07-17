using DotYou.AdminClient.Extensions;
using System;
using System.Security.Cryptography;
using System.Text;

namespace DotYou.Kernel.Cryptography
{
    public static class XorMgmt
    {
        /// <summary>
        /// Returns the XOR'ed key which you can e.g. safely store in the DB.
        /// </summary>
        /// <param name="token">Typically the client token. Shouldn't be on the server.</param>
        /// <param name="key">The secret key to protect.</param>
        /// <returns>The XOR'ed key</returns>
        public static byte[] xorKey(byte[] token, byte[] key)
        {
            return YFByteArray.EquiByteArrayXor(token, key);
        }

        /// <summary>
        /// Takes the XOR'ed key and returns the key
        /// </summary>
        /// <param name="token">Typically the client token. Shouldn't be on the server.</param>
        /// <param name="xorKey">The XOR'ed key</param>
        /// <returns>The secret key</returns>
        public static byte[] xorXorKey(byte[] token, byte[] xorKey)
        {
            return YFByteArray.EquiByteArrayXor(token, xorKey);
        }



        /// <param name="token"></param>
        /// <param name="xorKey">The XOR'ed key</param>
        /// <returns>The secret key</returns>


        /// <summary>
        /// Takes the XOR'ed key, an old token and a new token and returns the new xorKey
        /// </summary>
        /// <param name="oldToken">Typically the client token. Shouldn't be on the server. The token to replace</param>
        /// <param name="newToken">The new token to replace it with</param>
        /// <param name="xorKey">The new Xor key</param>
        /// <returns></returns>
        public static byte[] refreshToken(byte[] oldToken, byte[] newToken, byte[] xorKey)
        {
            var key = XorMgmt.xorXorKey(oldToken, xorKey);
            var newXorKey = XorMgmt.xorKey(newToken, key);
            YFByteArray.WipeByteArray(key);

            return newXorKey;
        }
    }

}
