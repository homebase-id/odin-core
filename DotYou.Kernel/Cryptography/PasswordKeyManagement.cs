using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System;
using System.IO;

namespace DotYou.Kernel.Cryptography
{
    public static class PasswordKeyManagement
    {
        /// <summary>
        /// Only call this on initializing an identity the first time 
        /// The KeK is the password pbkdf2'ed according to specs
        /// You should only call if the identity's PasswordKey data struct is null
        /// On creation the DeK will be set and encrypted with the KeK
        /// </summary>
        /// <param name="passwordKeK">pbkdf2(SaltKek, password, 100000, 16)</param>
        /// <returns></returns>
        private static PasswordKey CreateInitialPasswordKey(byte[] passwordKeK)
        {
            var passwordKey = new PasswordKey()
            {
                SaltPassword = YFByteArray.GetRndByteArray(16),
                SaltKek = YFByteArray.GetRndByteArray(16),
                HashPassword = YFByteArray.GetRndByteArray(16)
            };

            var DeK = YFByteArray.GetRndByteArray(16); // Create the DeK
            passwordKey.XorEncryptedDek = XorManagement.XorEncrypt(DeK, passwordKeK);
            YFByteArray.WipeByteArray(DeK);

            return passwordKey;
        }


        public static void ChangePassword(PasswordKey passwordKey, byte[] oldKeK, byte[] newKeK)
        {
            var DeK = GetDek(passwordKey, oldKeK);
            passwordKey.XorEncryptedDek = XorManagement.XorEncrypt(DeK, newKeK);
            YFByteArray.WipeByteArray(DeK);
        }

        public static byte[] GetDek(PasswordKey passwordKey, byte[] KeK)
        {
            return XorManagement.XorEncrypt(passwordKey.XorEncryptedDek, KeK);
        }

        /// <summary>
        /// I'm undecided if this should be in a NonceManagement class. But I mashed it into 
        /// the PasswordKey class.
        /// Used to set the initial password.
        /// On the server when you receive a PasswordReply and you have loaded the corresponding
        /// Nonce package, then call here to 
        /// </summary>
        /// <param name="loadedNoncePackage"></param>
        /// <param name="reply"></param>
        /// <returns>The PasswordKey to store on the Identity</returns>
        public static PasswordKey SetInitialPassword(NoncePackage loadedNoncePackage, PasswordReply reply)
        {
            // Perform a sanity check:
            // Make sure that the NonceHashedPassword64 calculated on the client 
            // also has the same calculation on the server.

            var b1 = Convert.FromBase64String(reply.NonceHashedPassword64);
            var b2 = KeyDerivation.Pbkdf2(
                reply.HashedPassword64,
                Convert.FromBase64String(reply.Nonce64),
                KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS,
                CryptographyConstants.HASH_SIZE);

            if (YFByteArray.EquiByteArrayCompare(b1, b2) == false)
            {
                throw new InvalidDataException("NonceHashedPassword sanity mismatch");
            }

            return PasswordKeyManagement.CreateInitialPasswordKey(Convert.FromBase64String(loadedNoncePackage.SaltKek64));
        }

        /// <summary>
        /// Test is the received nonceHashedPassword64 matches up with hashing the stored
        /// hasedPassword with the Nonce. If they do, the password is a match.
        /// </summary>
        /// <param name="pk">The PasswordKey stored on the Identity</param>
        /// <param name="nonceHashedPassword64">The client calculated nonceHashedPassword64</param>
        /// <param name="nonce64">The nonce the client was given by the server</param>
        /// <returns></returns>
        public static bool IsPasswordKeyMatch(PasswordKey pk, string nonceHashedPassword64, string nonce64)
        {
            var noncePasswordBytes = Convert.FromBase64String(nonceHashedPassword64);

            var nonceHashedPassword = KeyDerivation.Pbkdf2(
                Convert.ToBase64String(pk.HashPassword),
                Convert.FromBase64String(nonce64),
                KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS,
                CryptographyConstants.HASH_SIZE);

            return YFByteArray.EquiByteArrayCompare(noncePasswordBytes, nonceHashedPassword);
        }
    }

}
