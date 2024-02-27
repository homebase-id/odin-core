using System;
using System.Security.Cryptography;
using System.Text;
using Odin.Core.Cryptography.Data;
using Org.BouncyCastle.Security;

namespace Odin.Core.Cryptography.Crypto
{
    public static class RsaKeyManagement
    {
        public static int noKeysCreated = 0;
        public static int noKeysExpired = 0;
        public static int noKeysCreatedTest = 0;
        public static int noEncryptions = 0;
        public static int noDecryptions = 0;

        public static int noDBOpened = 0;
        public static int noDBClosed = 0;


        // Not a good place for this function. Should be in some of the login stuff... ?
        // I think this is the helper function for login, i.e. replicating what's normally in JS.
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

    public static class EccKeyManagement
    {
        public static int noKeysCreated = 0;
        public static int noKeysExpired = 0;
        public static int noKeysCreatedTest = 0;
        public static int noEncryptions = 0;
        public static int noDecryptions = 0;

        public static int noDBOpened = 0;
        public static int noDBClosed = 0;
    }
}
