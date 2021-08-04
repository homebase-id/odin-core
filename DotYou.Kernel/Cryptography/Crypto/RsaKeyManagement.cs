using System;
using System.Security.Cryptography;
using DotYou.AdminClient.Extensions;
using DotYou.Kernel.Services.Admin.Authentication;

namespace DotYou.Kernel.Cryptography
{

    // So it's slightly messy to mix up the version with encrypted and unencrypted private key.
    // Not sure if I should break it into two almost identical classes.
    public static class RsaKeyManagement
    {
        // Work to do here. OAEP or for signing? Encrypted private?
        public static RsaKeyData CreateKey(int hours, int minutes=0, int seconds=0)
        {
            var rsa = new RsaKeyData();

            rsa.encrypted = false;
            rsa.iv = Guid.Empty;

            RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            rsa.privateKey = rsaGenKeys.ExportRSAPrivateKey();
            rsa.publicKey = rsaGenKeys.ExportRSAPublicKey();
            rsa.crc32c = KeyCRC(rsaGenKeys);
            rsa.instantiated = DateTimeExtensions.UnixTime();
            rsa.expiration = rsa.instantiated + (UInt64)hours * 3600+ (UInt64)minutes*60+(UInt64)seconds;

            if (rsa.expiration <= rsa.instantiated)
                throw new Exception("Expiration must be > 0");

            return rsa;
        }

        public static bool IsExpired(RsaKeyData key)
        {
            UInt64 t = DateTimeExtensions.UnixTime();
            if (t > key.expiration)
                return true;
            else
                return false;
        }


        public static RSACryptoServiceProvider KeyPublic(RsaKeyData key)
        {
            int nBytesRead;

            RSACryptoServiceProvider rsaPublic = new RSACryptoServiceProvider();
            rsaPublic.ImportRSAPublicKey(key.publicKey, out nBytesRead);

            return rsaPublic;
        }

        public static RSACryptoServiceProvider KeyPrivate(RsaKeyData key)
        {
            int nBytesRead;

            RSACryptoServiceProvider rsaFull = new RSACryptoServiceProvider();
            rsaFull.ImportRSAPrivateKey(key.privateKey, out nBytesRead);

            return rsaFull;
        }

        public static UInt32 KeyCRC(RsaKeyData key)
        {
            return CRC32C.CalculateCRC32C(0, KeyPublic(key).ExportRSAPublicKey());
        }

        public static UInt32 KeyCRC(RSACryptoServiceProvider rsa)
        {
            return CRC32C.CalculateCRC32C(0, rsa.ExportRSAPublicKey());
        }

        public static string publicPem(RsaKeyData key)
        {
            // Either -- BEGIN RSA PUBLIC KEY -- and ExportRSAPublicKey
            // Or use -- BEGIN PUBLIC KEY -- and ExportSubjectPublicKeyInfo
            return "-----BEGIN PUBLIC KEY-----\n" + Convert.ToBase64String(KeyPublic(key).ExportSubjectPublicKeyInfo()) + "\n-----END PUBLIC KEY-----";
        }

        // privatePEM needs work in case it's encrypted
        public static string privatePem(RsaKeyData key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return "-----BEGIN PRIVATE KEY-----\n" + Convert.ToBase64String(KeyPrivate(key).ExportPkcs8PrivateKey()) + "\n-----END PRIVATE KEY-----";
        }


    }
}
