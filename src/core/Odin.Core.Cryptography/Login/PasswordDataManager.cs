using System;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;

namespace Odin.Core.Cryptography.Login
{
    public class DecryptedRSAPasswordHeader
    {
        public string hpwd64 { get; set; }
        public string kek64 { get; set; }
        public string secret { get; set; }
    }

    public static class PasswordDataManager
    {
        /// <summary>
        /// Only call this on initializing an identity the first time 
        /// The KeK is the password pbkdf2'ed according to specs
        /// You should only call if the identity's PasswordKey data struct is null
        /// On creation the DeK will be set and encrypted with the KeK
        /// </summary>
        /// <param name="passwordKeK">pbkdf2(SaltKek, password, CryptographyConstants.ITERATIONS, 16)</param>
        /// <returns></returns>
        private static PasswordData CreateInitialPasswordKey(NonceData nonce, string hashedPassword64, string kek64, SensitiveByteArray masterKey)
        {
            var passwordKey = new PasswordData()
            {
                SaltPassword = Convert.FromBase64String(nonce.SaltPassword64),
                SaltKek = Convert.FromBase64String(nonce.SaltKek64),
                HashPassword = Convert.FromBase64String(hashedPassword64)
            };

            // TODO: Hm, I really DONT like that we pass the KEK as a string.
            // gives me the shivers... I'll redo the client <-> server passing
            // so that we base64 encode the RSA encrypted string, rather than passing
            // a nice readable string over and then encrypting it. 
            // This way, once we RSA decrypt it is a byte array and we can zap it.

            // TODO: Change to using ()
            var kekKey = new SensitiveByteArray(Convert.FromBase64String(kek64));
            if(null == masterKey)
            {
                passwordKey.KekEncryptedMasterKey = new SymmetricKeyEncryptedAes(kekKey);
            }
            else
            {
                passwordKey.KekEncryptedMasterKey = new SymmetricKeyEncryptedAes(kekKey, masterKey);
            }
            
            kekKey.Wipe();

            return passwordKey;
        }

        public static void ChangePassword(PasswordData passwordKey, byte[] oldKeK, byte[] newKeK)
        {
            throw new Exception();

            // var DeK = GetDek(passwordKey, oldKeK);
            // passwordKey.XorEncryptedDek = XorManagement.XorEncrypt(DeK, newKeK);
            // ByteArrayUtil.WipeByteArray(DeK);
        }

        public static SensitiveByteArray GetDek(PasswordData passwordKey, SensitiveByteArray KeK)
        {
            return GetDek(passwordKey.KekEncryptedMasterKey, KeK);
        }

        public static SensitiveByteArray GetDek(SymmetricKeyEncryptedAes EncryptedDek, SensitiveByteArray KeK)
        {
            return EncryptedDek.DecryptKeyClone(KeK);
        }

        /// <summary>
        /// I'm undecided if this should be in a NonceManagement class. But I mashed it into 
        /// the PasswordKey class.
        /// Used to set the initial password.
        /// On the server when you receive a PasswordReply and you have loaded the corresponding
        /// Nonce package, then call here to setup everything needed (HasedPassword, Kek, DeK)
        /// </summary>
        /// <returns>The PasswordKey to store on the Identity</returns>
        public static PasswordData SetInitialPassword(NonceData loadedNoncePackage, PasswordReply reply, RsaFullKeyListData listRsa,
            SensitiveByteArray masterKey = null)
        {
            var (hpwd64, kek64, sharedsecret) = ParsePasswordRSAReply(reply, listRsa);

            TryPasswordKeyMatch(hpwd64, reply.NonceHashedPassword64, reply.Nonce64);

            var passwordKey = PasswordDataManager.CreateInitialPasswordKey(loadedNoncePackage, hpwd64, kek64, masterKey);


            return passwordKey;
        }


        // From the PasswordReply package received from the client, try to decrypt the RSA
        // encoded header and retrieve the hashedPassword, KeK, and SharedSecret values
        public static (string pwd64, string kek64, string sharedsecret64) ParsePasswordRSAReply(PasswordReply reply, RsaFullKeyListData listRsa)
        {
            // The nonce matches, now let's decrypt the RSA encoded header and set the data
            //
            var key = RsaKeyListManagement.FindKey(listRsa, reply.crc);

            if (key == null)
                throw new Exception("no matching RSA key");

            byte[] decryptedRSA;

            try
            {
                decryptedRSA = key.Decrypt(RsaKeyListManagement.zeroSensitiveKey, Convert.FromBase64String(reply.RsaEncrypted));
            }
            catch
            {
                throw new Exception("Unable to RSA decrypt password header");
            }

            string originalResult = Encoding.Default.GetString(decryptedRSA);

            // I guess / hope if it fails it throws an exception :-))
            //

            string hpwd64;
            string kek64;
            string sharedsecret64;
            try
            {
                //Note: had to use an explicit class since the System.Text.Json serializer failed with dynamic
                var o = OdinSystemSerializer.Deserialize<DecryptedRSAPasswordHeader>(originalResult);

                hpwd64 = o.hpwd64;
                kek64 = o.kek64;
                sharedsecret64 = o.secret;
            }
            catch
            {
                throw new Exception("Unable to parse the decrypted RSA password header");
            }

            if ((Convert.FromBase64String(hpwd64).Length != 16) ||
                (Convert.FromBase64String(kek64).Length != 16) ||
                (Convert.FromBase64String(sharedsecret64).Length != 16))
                throw new Exception("Base64 strings in password reply incorrect");

            return (hpwd64, kek64, sharedsecret64);
        }


        // Returns the kek64 and sharedSecret64 by the RSA encrypted reply from the client.
        // We should rename this function. The actual authentication is done in TryPasswordKeyMatch
        public static (byte[] kek64, byte[] sharedsecret64) Authenticate(NonceData loadedNoncePackage,
            PasswordReply reply, RsaFullKeyListData listRsa)
        {
            var (hpwd64, kek64, sharedsecret64) = ParsePasswordRSAReply(reply, listRsa);
            return (Convert.FromBase64String(kek64), Convert.FromBase64String(sharedsecret64));
        }


        public static void TryPasswordKeyMatch(string hashPassword64, string nonceHashedPassword64, string nonce64)
        {
            var noncePasswordBytes = Convert.FromBase64String(nonceHashedPassword64);

            var nonceHashedPassword = KeyDerivation.Pbkdf2(
                hashPassword64,
                Convert.FromBase64String(nonce64),
                KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS,
                CryptographyConstants.HASH_SIZE);

            if (ByteArrayUtil.EquiByteArrayCompare(noncePasswordBytes, nonceHashedPassword) == false)
                throw new OdinSecurityException("Password mismatch");
        }


        /// <summary>
        /// Test is the received nonceHashedPassword64 matches up with hashing the stored
        /// hasedPassword with the Nonce. If they do, the password is a match.
        /// </summary>
        /// <param name="pk">The PasswordKey stored on the Identity</param>
        /// <param name="nonceHashedPassword64">The client calculated nonceHashedPassword64</param>
        /// <param name="nonce64">The nonce the client was given by the server</param>
        /// <returns></returns>
        public static void TryPasswordKeyMatch(PasswordData pk, string nonceHashedPassword64, string nonce64)
        {
            TryPasswordKeyMatch(Convert.ToBase64String(pk.HashPassword), nonceHashedPassword64, nonce64);
        }


        public static PasswordData SetInitialPassword(NonceData noncePackage, object loadedNoncePackage,
            PasswordReply passwordReply, object reply)
        {
            throw new NotImplementedException();
        }

        public static PasswordReply CalculatePasswordReply(string password, NonceData nonce)
        {
            var pr = new PasswordReply();

            pr.Nonce64 = nonce.Nonce64;

            string hashedPassword64 = Convert.ToBase64String(KeyDerivation.Pbkdf2(password,
                Convert.FromBase64String(nonce.SaltPassword64), KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE));
            string keK64 = Convert.ToBase64String(KeyDerivation.Pbkdf2(password,
                Convert.FromBase64String(nonce.SaltKek64), KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE));
            pr.NonceHashedPassword64 = Convert.ToBase64String(KeyDerivation.Pbkdf2(hashedPassword64,
                Convert.FromBase64String(nonce.Nonce64), KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS,
                CryptographyConstants.HASH_SIZE));

            //TODO XXX
            //RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
            //rsa.ImportFromPem(nonce.PublicPem.ToCharArray());
            //pr.crc = RsaKeyManagement.KeyCRC(rsa);

            var data = new
            {
                hpwd64 = hashedPassword64,
                kek64 = keK64,
                secret = ByteArrayUtil.GetRndByteArray(16)
            };
            var str = OdinSystemSerializer.Serialize(data);

            (pr.crc, pr.RsaEncrypted) = RsaKeyManagement.PasswordCalculateReplyHelper(nonce.PublicPem, str);

            // If the login is successful then the client will get the cookie
            // and will have to use this sharedsecret on all requests. So store securely in 
            // local storage.

            return pr;
        }
    }
}