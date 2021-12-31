using System;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Newtonsoft.Json;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Cryptography
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
                SaltKek = Convert.FromBase64String(nonce.SaltKek64),
                HashPassword = Convert.FromBase64String(HashedPassword64)
            };

            // TODO: Hm, I really DONT like that we pass the KEK as a string.
            // gives me the shivers... I'll redo the client <-> server passing
            // so that we base64 encode the RSA encrypted string, rather than passing
            // a nice readable string over and then encrypting it. 
            // This way, once we RSA decrypt it is a byte array and we can zap it.
            var KekKey = new SecureKey(Convert.FromBase64String(KeK64));
            passwordKey.EncryptedDek = new SymmetricKeyEncryptedAes(KekKey);

            return passwordKey;
        }

        public static void ChangePassword(LoginKeyData passwordKey, byte[] oldKeK, byte[] newKeK)
        {
            throw new Exception();

            // var DeK = GetDek(passwordKey, oldKeK);
            // passwordKey.XorEncryptedDek = XorManagement.XorEncrypt(DeK, newKeK);
            // ByteArrayUtil.WipeByteArray(DeK);
        }

        public static SecureKey GetDek(LoginKeyData passwordKey, byte[] KeK)
        {
            return GetDek(passwordKey.EncryptedDek, KeK);
        }

        public static SecureKey GetDek(SymmetricKeyEncryptedAes EncryptedDek, byte[] KeK)
        {
            return EncryptedDek.DecryptKey(KeK);
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
            var (hpwd64, kek64, sharedsecret) = ParsePasswordRSAReply(reply, listRsa);

            TryPasswordKeyMatch(hpwd64, reply.NonceHashedPassword64, reply.Nonce64);

            var passwordKey = LoginKeyManager.CreateInitialPasswordKey(loadedNoncePackage, hpwd64, kek64);

            
            return passwordKey;
        }


        // From the PasswordReply package received from the client, try to decrypt the RSA
        // encoded header and retrieve the hashedPassword, KeK, and SharedSecret values
        public static (string pwd64, string kek64, string sharedsecret64) ParsePasswordRSAReply(IPasswordReply reply,
            RsaKeyListData listRsa)
        {
            // The nonce matches, now let's decrypt the RSA encoded header and set the data
            //
            var key = RsaKeyListManagement.FindKey(listRsa, reply.crc);

            if (key == null)
                throw new Exception("no matching RSA key");

            byte[] decryptedRSA;

            try
            {
                decryptedRSA = key.Decrypt(Convert.FromBase64String(reply.RsaEncrypted));
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


        // Returns the kek64 and sharedSecret64 by the RSA encrypted reply from the client.
        // We should rename this function. The actual authentication is done in TryPasswordKeyMatch
        public static (byte[] kek64, byte[] sharedsecret64) Authenticate(NonceData loadedNoncePackage,
            IPasswordReply reply, RsaKeyListData listRsa)
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


        public static LoginKeyData SetInitialPassword(NonceData noncePackage, object loadedNoncePackage,
            PasswordReply passwordReply, object reply)
        {
            throw new NotImplementedException();
        }

        public static PasswordReply CalculatePasswordReply(string password, NonceData nonce)
        {
            var pr = new PasswordReply();

            pr.Nonce64 = nonce.Nonce64;

            string HashedPassword64 = Convert.ToBase64String(KeyDerivation.Pbkdf2(password,
                Convert.FromBase64String(nonce.SaltPassword64), KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE));
            string KeK64 = Convert.ToBase64String(KeyDerivation.Pbkdf2(password,
                Convert.FromBase64String(nonce.SaltKek64), KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE));
            pr.NonceHashedPassword64 = Convert.ToBase64String(KeyDerivation.Pbkdf2(HashedPassword64,
                Convert.FromBase64String(nonce.Nonce64), KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS,
                CryptographyConstants.HASH_SIZE));

            //TODO XXX
            //RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
            //rsa.ImportFromPem(nonce.PublicPem.ToCharArray());
            //pr.crc = RsaKeyManagement.KeyCRC(rsa);

            var data = new
            {
                hpwd64 = HashedPassword64,
                kek64 = KeK64,
                secret = ByteArrayUtil.GetRndByteArray(16)
            };
            var str = JsonConvert.SerializeObject(data);

            (pr.crc, pr.RsaEncrypted) = RsaKeyManagement.PasswordCalculateReplyHelper(nonce.PublicPem, str);

            // If the login is successful then the client will get the cookie
            // and will have to use this sharedsecret on all requests. So store securely in 
            // local storage.

            return pr;
        }
    }
}