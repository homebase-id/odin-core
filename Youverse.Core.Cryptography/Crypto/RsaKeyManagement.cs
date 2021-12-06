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
            // Generate an asymmetric key with BC, 2048 bits
            RsaKeyPairGenerator r = new RsaKeyPairGenerator();
            r.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
            AsymmetricCipherKeyPair keys = r.GenerateKeyPair();

            // Extract the public and the private keys
            var privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keys.Private);
            var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keys.Public);

            // Create and prepare our own RsaKeyData data structure
            var rsa = new RsaKeyData();
            rsa.encrypted = false;
            rsa.iv = Guid.Empty;

            // Save the DER encoded private and public keys in our own data structure
            rsa.privateKey = privateKeyInfo.GetDerEncoded();
            rsa.publicKey  = publicKeyInfo.GetDerEncoded();

            rsa.crc32c = KeyCRC(rsa);
            rsa.instantiated = DateTimeExtensions.UnixTimeSeconds();
            rsa.expiration = rsa.instantiated + (UInt64)hours * 3600+ (UInt64)minutes*60+(UInt64)seconds;

            if (rsa.expiration <= rsa.instantiated)
                throw new Exception("Expiration must be > 0");

            return rsa;
        }

        private static UInt32 KeyCRC(byte[] keyDerEncoded)
        {
            return CRC32C.CalculateCRC32C(0, keyDerEncoded);
        }

        private static UInt32 KeyCRC(RsaKeyData key)
        {
            return KeyCRC(key.publicKey);
        }

        public static string publicDerBase64(RsaKeyData key)
        {
            // Either -- BEGIN RSA PUBLIC KEY -- and ExportRSAPublicKey
            // Or use -- BEGIN PUBLIC KEY -- and ExportSubjectPublicKeyInfo
            return Convert.ToBase64String(key.publicKey);
        }

        public static string privateDerBase64(RsaKeyData key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return Convert.ToBase64String(key.privateKey);
        }

        public static string publicPem(RsaKeyData key)
        {
            // Either -- BEGIN RSA PUBLIC KEY -- and ExportRSAPublicKey
            // Or use -- BEGIN PUBLIC KEY -- and ExportSubjectPublicKeyInfo
            return "-----BEGIN PUBLIC KEY-----\n" + publicDerBase64(key) + "\n-----END PUBLIC KEY-----";
        }

        // privatePEM needs work in case it's encrypted
        public static string privatePem(RsaKeyData key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return "-----BEGIN PRIVATE KEY-----\n" + privateDerBase64(key) + "\n-----END PRIVATE KEY-----";
        }

        public static byte[] decodePublicPem(string key)
        {
            string publicKeyPEM = key.Replace("-----BEGIN PUBLIC KEY-----", "")
                                        .Replace("\n", "")
                                        .Replace("\r", "")
                                        .Replace("-----END PUBLIC KEY-----", "");

            return Convert.FromBase64String(publicKeyPEM);
        }


        // Encrypt with the public key
        public static byte[] Encrypt(RsaKeyData key, byte[] data)
        {
            var publicKeyRestored = PublicKeyFactory.CreateKey(key.publicKey);

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(true, publicKeyRestored);
            var cipherData = cipher.DoFinal(data);

            return cipherData;
        }

        // Decrypt with private key
        public static byte[] Decrypt(RsaKeyData key, byte[] cipherData)
        {
            var privateKeyRestored = PrivateKeyFactory.CreateKey(key.privateKey);

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(false, privateKeyRestored);

            var clearData = cipher.DoFinal(cipherData);
 
            return clearData;
        }

        public static (UInt32 crc, string rsaCipher64) PasswordCalculateReplyHelper(string publicKey, string payload)
        {
            var rsa = new RSACng();
            rsa.ImportFromPem(publicKey);

            var publicKeyRestored = PublicKeyFactory.CreateKey(decodePublicPem(publicKey));

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(true, publicKeyRestored);
            var tmp = cipher.DoFinal(Encoding.ASCII.GetBytes(payload));

            var cipherData = Convert.ToBase64String(tmp);


            return (RsaKeyManagement.KeyCRC(decodePublicPem(publicKey)), cipherData);
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

    }
}
