using System;
using System.Security.Cryptography;
using System.Text;
using Youverse.Core.Cryptography.Data;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Youverse.Core.Cryptography.Crypto
{
    // Unfortunately, the C# class RSACng() is the only class compatible with
    // the javaScript crypto.subtle. So we had to implement BouncyCastle for RSA OAEP.

    public static class RsaKeyManagement
    {
        /// <summary>
        /// This is right now a hack for TESTING only. If this is needed then
        /// we should work that out with CreateKey()
        /// </summary>
        /// </summary>
        /// <param name="key"></param>
        /// <param name="derEncodedPrivateKey"></param>
        public static void SetFullKey(RsaFullKeyData key, byte[] derEncodedPrivateKey)
        {
            key.privateKey = derEncodedPrivateKey;
            var pkRestored = PublicKeyFactory.CreateKey(key.publicKey);
            var pk = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pkRestored);
            key.publicKey = pk.GetDerEncoded();
        }

        // Not a good place for this function. Should be in some of the login stuff... ?
        public static (UInt32 crc, string rsaCipher64) PasswordCalculateReplyHelper(string publicKey, string payload)
        {
            var publicKeyRestored = PublicKeyFactory.CreateKey(RsaPublicKeyData.decodePublicPem(publicKey));

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(true, publicKeyRestored);
            var tmp = cipher.DoFinal(Encoding.ASCII.GetBytes(payload));

            var cipherData = Convert.ToBase64String(tmp);

            return (RsaPublicKeyData.KeyCRC(RsaPublicKeyData.decodePublicPem(publicKey)), cipherData);
        }
    }
}
