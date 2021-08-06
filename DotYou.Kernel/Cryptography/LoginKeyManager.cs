using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DotYou.Kernel.Cryptography
{
    public static class LoginKeyManager
    {
        /// <summary>
        /// Only call this on initializing an identity the first time 
        /// The KeK is the password pbkdf2'ed according to specs
        /// You should only call if the identity's PasswordKey data struct is null
        /// On creation the DeK will be set and encrypted with the KeK
        /// </summary>
        /// <param name="passwordKeK">pbkdf2(SaltKek, password, 100000, 16)</param>
        /// <returns></returns>
        private static LoginKeyData CreateInitialPasswordKey(NonceData nonce, string HashedPassword64, string KeK64)
        {
            var passwordKey = new LoginKeyData()
            {
                SaltPassword = Convert.FromBase64String(nonce.SaltPassword64),
                SaltKek      = Convert.FromBase64String(nonce.SaltKek64),
                HashPassword = Convert.FromBase64String(HashedPassword64)
            };

            var DeK = YFByteArray.GetRndByteArray(16); // Create the DeK
            passwordKey.XorEncryptedDek = XorManagement.XorEncrypt(DeK, Convert.FromBase64String(KeK64));
            YFByteArray.WipeByteArray(DeK);

            return passwordKey;
        }


        public static void ChangePassword(LoginKeyData passwordKey, byte[] oldKeK, byte[] newKeK)
        {
            var DeK = GetDek(passwordKey, oldKeK);
            passwordKey.XorEncryptedDek = XorManagement.XorEncrypt(DeK, newKeK);
            YFByteArray.WipeByteArray(DeK);
        }

        public static byte[] GetDek(LoginKeyData passwordKey, byte[] KeK)
        {
            return XorManagement.XorEncrypt(passwordKey.XorEncryptedDek, KeK);
        }

        /// <summary>
        /// I'm undecided if this should be in a NonceManagement class. But I mashed it into 
        /// the PasswordKey class.
        /// Used to set the initial password.
        /// On the server when you receive a PasswordReply and you have loaded the corresponding
        /// Nonce package, then call here to setup everything needed (HasedPassword, Kek, DeK)
        /// </summary>
        /// <param name="loadedNoncePackage"></param>
        /// <param name="reply"></param>
        /// <returns>The PasswordKey to store on the Identity</returns>
        public static LoginKeyData SetInitialPassword(NonceData loadedNoncePackage, PasswordReply reply, RsaKeyListData listRsa)
        {
            var (hpwd64, kek64, sharedsecret) = ParsePasswordReply(reply, listRsa);

            TryPasswordKeyMatch(hpwd64, reply.NonceHashedPassword64, reply.Nonce64);

            var passwordKey = LoginKeyManager.CreateInitialPasswordKey(loadedNoncePackage, hpwd64, kek64);

            return passwordKey;
        }


        // From the PasswordReply package received from the client, try to decrypt the RSA
        // encoded header and retrieve the hashedPassword, KeK, and SharedSecret values
        private static (string pwd64, string kek64, string sharedsecret64) ParsePasswordReply(PasswordReply reply, RsaKeyListData listRsa)
        {
            // The nonce matches, now let's decrypt the RSA encoded header and set the data
            //
            RSACryptoServiceProvider rsa = RsaKeyListManagement.FindKeyPrivate(listRsa, reply.crc);

            if (rsa == null)
                throw new Exception("no matching RSA key");

            byte[] decryptedRSA;

            try
            {
                decryptedRSA = rsa.Decrypt(Convert.FromBase64String(reply.RsaEncrypted), true);
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
                var o = JsonConvert.DeserializeObject<dynamic>(originalResult);

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


        // Returns the shared secret if authenticated or throws an exception otherwise
        public static (byte[] kek64, byte[] sharedsecret64) Authenticate(NonceData loadedNoncePackage, PasswordReply reply, RsaKeyListData listRsa)
        {
            var (hpwd64, kek64, sharedsecret64) = ParsePasswordReply(reply, listRsa);

            TryPasswordKeyMatch(hpwd64, reply.NonceHashedPassword64, reply.Nonce64);

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

            if (YFByteArray.EquiByteArrayCompare(noncePasswordBytes, nonceHashedPassword) == false)
                throw new Exception("Password mismatch");
        }


        /// <summary>
        /// Test is the received nonceHashedPassword64 matches up with hashing the stored
        /// hasedPassword with the Nonce. If they do, the password is a match.
        /// </summary>
        /// <param name="pk">The PasswordKey stored on the Identity</param>
        /// <param name="nonceHashedPassword64">The client calculated nonceHashedPassword64</param>
        /// <param name="nonce64">The nonce the client was given by the server</param>
        /// <returns></returns>
        public static void TryPasswordKeyMatch(LoginKeyData pk, string nonceHashedPassword64, string nonce64)
        {
            TryPasswordKeyMatch(Convert.ToBase64String(pk.HashPassword), nonceHashedPassword64, nonce64);
        }


        public static LoginKeyData SetInitialPassword(NonceData noncePackage, object loadedNoncePackage, PasswordReply passwordReply, object reply)
        {
            throw new NotImplementedException();
        }

        public static PasswordReply CalculatePasswordReply(string password, NonceData nonce)
        {
            var pr = new PasswordReply();

            pr.Nonce64 = nonce.Nonce64;

            string HashedPassword64 = Convert.ToBase64String(KeyDerivation.Pbkdf2(password, Convert.FromBase64String(nonce.SaltPassword64), KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE));
            string KeK64            = Convert.ToBase64String(KeyDerivation.Pbkdf2(password, Convert.FromBase64String(nonce.SaltKek64),      KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE));
            pr.NonceHashedPassword64 = Convert.ToBase64String(KeyDerivation.Pbkdf2(HashedPassword64, Convert.FromBase64String(nonce.Nonce64), KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE));

            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
            rsa.ImportFromPem(nonce.PublicPem.ToCharArray());
            pr.crc = RsaKeyManagement.KeyCRC(rsa);

            var data = new {
                hpwd64 = HashedPassword64,
                kek64  = KeK64,
                secret = YFByteArray.GetRndByteArray(16)
            };
            var str = JsonConvert.SerializeObject(data);

            pr.RsaEncrypted = Convert.ToBase64String(rsa.Encrypt(Encoding.ASCII.GetBytes(str), true));

            // If the login is successful then the client will get the cookie
            // and will have to use this sharedsecret on all requests. So store securely in 
            // local storage.

            return pr;
        }
    }
}

