using System;
using System.Security.Cryptography;
using System.Text;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Cryptography.Crypto
{
    // Unfortunately, the C# class RSACng() is the only class compatible with
    // the javaScript crypto.subtle. We need that compatibility. So either poke
    // Microsoft to make a cross-platform RSACng() or add in something that can.
    
    //
    // Here's how to switch to BouncyCastle (BC) to get around the RSACng. Guess I am hoping
    // .NET Core 6 might solve this so we don't need to implement BC.
    // https://stackoverflow.com/questions/46916718/oaep-padding-error-when-decrypting-data-in-c-sharp-that-was-encrypted-in-javascr


    // So it's slightly messy to mix up the version with encrypted and unencrypted private key.
    // Not sure if I should break it into two almost identical classes (or an 'interface'?).
    // Right now only used for the temp key for login. Will also need a third one for signing
    //
    public static class RsaKeyManagement
    {
        // Work to do here. OAEP or for signing? Encrypted private?
        public static RsaKeyData CreateKey(int hours, int minutes=0, int seconds=0)
        {
            var rsa = new RsaKeyData();

            rsa.encrypted = false;
            rsa.iv = Guid.Empty;

            var rsaGenKeys = new RSACng(2048);   // Windows only **wwaaahhh*** Need to figure that one out

            // rsa.privateKey = rsaGenKeys.ExportRSAPrivateKey();
            // rsa.publicKey = rsaGenKeys.ExportRSAPublicKey();

            rsa.privateKey = rsaGenKeys.ExportPkcs8PrivateKey();
            rsa.publicKey  = rsaGenKeys.ExportSubjectPublicKeyInfo();  // Be JS crypto.subtle compatible

            rsa.crc32c = KeyCRC(rsa);
            rsa.instantiated = DateTimeExtensions.UnixTimeSeconds();
            rsa.expiration = rsa.instantiated + (UInt64)hours * 3600+ (UInt64)minutes*60+(UInt64)seconds;

            if (rsa.expiration <= rsa.instantiated)
                throw new Exception("Expiration must be > 0");

            return rsa;
        }

        public static byte[] Encrypt(RsaKeyData key, byte[] data, bool EncryptWithPublicKey)
        {
            RSACng myRsa;

            if (EncryptWithPublicKey)
                myRsa = KeyPublic(key);
            else
                myRsa = KeyPrivate(key);

            var cipher = myRsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);

            return cipher;
        }

        public static byte[] Decrypt(RsaKeyData key, byte[] cipher, bool DecryptWithPublicKey)
        {
            RSACng myRsa;

            if (DecryptWithPublicKey)
                myRsa = KeyPublic(key);
            else
                myRsa = KeyPrivate(key);

            var data = myRsa.Decrypt(cipher, RSAEncryptionPadding.OaepSHA256);

            return data;
        }

        public static (UInt32 crc, string rsaCipher64) PasswordCalculateReplyHelper(string publicKey, string payload)
        {
            var rsa = new RSACng();
            rsa.ImportFromPem(publicKey);
            var cipher = Convert.ToBase64String(rsa.Encrypt(Encoding.ASCII.GetBytes(payload), RSAEncryptionPadding.OaepSHA256));

            return (RsaKeyManagement.KeyCRC(rsa), cipher);
        }

        // Not expired, it's still good (it may be overdue for a refresh)
        public static bool IsValid(RsaKeyData key)
        {
            UInt64 t = DateTimeExtensions.UnixTimeSeconds();
            if (t <= key.expiration)
                return true;
            else
                return false;
        }


        public static bool IsExpired(RsaKeyData key)
        {
            UInt64 t = DateTimeExtensions.UnixTimeSeconds();
            if (t > key.expiration)
                return true;
            else
                return false;
        }


        // If more than twice the longevity beyond the expiration, or at most 24 hours beyond expiration, 
        // then the key is considered dead and will be removed
        public static bool IsDead(RsaKeyData key)
        {
            UInt64 t = DateTimeExtensions.UnixTimeSeconds();
            UInt64 d = Math.Min(2 * (key.expiration - key.instantiated), 3600*24) + key.instantiated;

            if (t > d)
                return true;
            else
                return false;
        }

        public static RSACng KeyPublic(RsaKeyData key)
        {
            var rsaPublic = new RSACng();

            rsaPublic.ImportSubjectPublicKeyInfo(key.publicKey, out int _);

            return rsaPublic;
        }

        public static RSACng KeyPrivate(RsaKeyData key)
        {
            var rsaFull = new RSACng();

            rsaFull.ImportPkcs8PrivateKey(key.privateKey, out int _);

            return rsaFull;
        }

        private static UInt32 KeyCRC(RsaKeyData key)
        {
            return CRC32C.CalculateCRC32C(0, key.publicKey);
        }

        public static UInt32 KeyCRC(RSACng rsa)
        {
            return CRC32C.CalculateCRC32C(0, rsa.ExportSubjectPublicKeyInfo());
        }

        public static string publicBase64(RsaKeyData key)
        {
            // Either -- BEGIN RSA PUBLIC KEY -- and ExportRSAPublicKey
            // Or use -- BEGIN PUBLIC KEY -- and ExportSubjectPublicKeyInfo
            return Convert.ToBase64String(key.publicKey);
        }

        // privatePEM needs work in case it's encrypted
        public static string privateBase64(RsaKeyData key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return Convert.ToBase64String(key.privateKey);
        }

        public static string publicPem(RsaKeyData key)
        {
            // Either -- BEGIN RSA PUBLIC KEY -- and ExportRSAPublicKey
            // Or use -- BEGIN PUBLIC KEY -- and ExportSubjectPublicKeyInfo
            return "-----BEGIN PUBLIC KEY-----\n" + publicBase64(key) + "\n-----END PUBLIC KEY-----";
        }

        // privatePEM needs work in case it's encrypted
        public static string privatePem(RsaKeyData key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return "-----BEGIN PRIVATE KEY-----\n" + privateBase64(key)+ "\n-----END PRIVATE KEY-----";
        }
    }
}
