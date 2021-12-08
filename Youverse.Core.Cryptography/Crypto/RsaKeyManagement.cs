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
        public static void SetPublicKey(RsaPublicKeyData key, byte[] derEncodedPublicKey)
        {
            key.publicKey = derEncodedPublicKey;
        }

        /// <summary>
        /// This is right now a hack for TESTING only. If this is needed then
        /// we should work that out with CreateKey()
        /// </summary>
        /// </summary>
        /// <param name="key"></param>
        /// <param name="derEncodedPrivateKey"></param>
        public static void SetFullKey(RsaFullKeyData key, byte[] derEncodedPrivateKey)
        {
            //key.privateKeyData.privateKey.Wipe();
            key.privateKeyData.privateKey = derEncodedPrivateKey;
            var pkRestored = PublicKeyFactory.CreateKey(key.publicKey);
            var pk = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pkRestored);
            key.publicKey = pk.GetDerEncoded();
        }

        // Work to do here. OAEP or for signing? Encrypted private?
        public static RsaFullKeyData CreateKey(int hours, int minutes = 0, int seconds = 0)
        {
            // Generate with BC an asymmetric key with BC, 2048 bits
            RsaKeyPairGenerator r = new RsaKeyPairGenerator();
            r.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
            AsymmetricCipherKeyPair keys = r.GenerateKeyPair();

            // Extract the public and the private keys
            var privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keys.Private);
            var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keys.Public);

            // Create and prepare our own RsaKeyData data structure
            var rsa = new RsaFullKeyData();
            rsa.privateKeyData = new RsaPrivateKeyData();

            // Save the DER encoded private and public keys in our own data structure
            rsa.privateKeyData.createdTimeStamp = DateTimeExtensions.UnixTimeSeconds();
            rsa.privateKeyData.privateKey = privateKeyInfo.GetDerEncoded();

            rsa.publicKey = publicKeyInfo.GetDerEncoded();
            rsa.crc32c = KeyCRC(rsa.publicKey);
            rsa.expiration = rsa.privateKeyData.createdTimeStamp + (UInt64)hours * 3600 + (UInt64)minutes * 60 + (UInt64)seconds;

            if (rsa.expiration <= rsa.privateKeyData.createdTimeStamp)
                throw new Exception("Expiration must be > 0");

            return rsa;
        }

        private static UInt32 KeyCRC(byte[] keyDerEncoded)
        {
            return CRC32C.CalculateCRC32C(0, keyDerEncoded);
        }

        private static UInt32 KeyCRC(RsaPublicKeyData key)
        {
            return KeyCRC(key.publicKey);
        }

        public static string publicDerBase64(RsaPublicKeyData key)
        {
            // Either -- BEGIN RSA PUBLIC KEY -- and ExportRSAPublicKey
            // Or use -- BEGIN PUBLIC KEY -- and ExportSubjectPublicKeyInfo
            return Convert.ToBase64String(key.publicKey);
        }

        public static string privateDerBase64(RsaFullKeyData key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return Convert.ToBase64String(key.privateKeyData.privateKey);
        }

        public static string publicPem(RsaPublicKeyData key)
        {
            // Either -- BEGIN RSA PUBLIC KEY -- and ExportRSAPublicKey
            // Or use -- BEGIN PUBLIC KEY -- and ExportSubjectPublicKeyInfo
            return "-----BEGIN PUBLIC KEY-----\n" + publicDerBase64(key) + "\n-----END PUBLIC KEY-----";
        }

        // privatePEM needs work in case it's encrypted
        public static string privatePem(RsaFullKeyData key)
        {
            // Either -----BEGIN RSA PRIVATE KEY----- and ExportRSAPrivateKey()
            // Or use -- BEGIN PRIVATE KEY -- and ExportPkcs8PrivateKey
            return "-----BEGIN PRIVATE KEY-----\n" + privateDerBase64(key) + "\n-----END PRIVATE KEY-----";
        }

        public static byte[] decodePublicPem(string pem)
        {
            string publicKeyPEM = pem.Replace("-----BEGIN PUBLIC KEY-----", "")
                                        .Replace("\n", "")
                                        .Replace("\r", "")
                                        .Replace("-----END PUBLIC KEY-----", "");

            return Convert.FromBase64String(publicKeyPEM);
        }


        // Encrypt with the public key
        public static byte[] Encrypt(RsaPublicKeyData key, byte[] data)
        {
            var publicKeyRestored = PublicKeyFactory.CreateKey(key.publicKey);

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(true, publicKeyRestored);
            var cipherData = cipher.DoFinal(data);

            return cipherData;
        }

        // Decrypt with private key
        public static byte[] Decrypt(RsaFullKeyData key, byte[] cipherData)
        {
            var privateKeyRestored = PrivateKeyFactory.CreateKey(key.privateKeyData.privateKey);

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(false, privateKeyRestored);

            var clearData = cipher.DoFinal(cipherData);

            return clearData;
        }

        public static (UInt32 crc, string rsaCipher64) PasswordCalculateReplyHelper(string publicKey, string payload)
        {
            var publicKeyRestored = PublicKeyFactory.CreateKey(decodePublicPem(publicKey));

            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(true, publicKeyRestored);
            var tmp = cipher.DoFinal(Encoding.ASCII.GetBytes(payload));

            var cipherData = Convert.ToBase64String(tmp);


            return (RsaKeyManagement.KeyCRC(decodePublicPem(publicKey)), cipherData);
        }

        public static bool IsExpired(RsaPublicKeyData key)
        {
            UInt64 t = DateTimeExtensions.UnixTimeSeconds();
            if (t > key.expiration)
                return true;
            else
                return false;
        }


        // Not expired, it's still good (it may be overdue for a refresh)
        public static bool IsValid(RsaPublicKeyData key)
        {
            return !IsExpired(key);
        }


        // If more than twice the longevity beyond the expiration, or at most 24 hours beyond expiration, 
        // then the key is considered dead and will be removed
        public static bool IsDead(RsaFullKeyData key)
        {
            UInt64 t = DateTimeExtensions.UnixTimeSeconds();
            UInt64 d = Math.Min(2 * (key.expiration - key.privateKeyData.createdTimeStamp), 3600 * 24) + key.privateKeyData.createdTimeStamp;

            if (t > d)
                return true;
            else
                return false;
        }
    }
}
