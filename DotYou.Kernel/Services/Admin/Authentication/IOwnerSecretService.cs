﻿using System.Threading.Tasks;
using DotYou.Kernel.Cryptography;
using DotYou.Types;
using DotYou.Types.Cryptography;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    public interface IOwnerSecretService
    {
        /// <summary>
        /// Generates two 16 byte crypto-random numbers used for salting passwords
        /// </summary>
        /// <returns></returns>
        Task<NonceData> GenerateNewSalts();

        Task SetNewPassword(PasswordReply reply);

        /// <summary>
        /// Returns the stored salts for the tenant
        /// </summary>
        /// <returns></returns>
        Task<SaltsPackage> GetStoredSalts();

        /// <summary>
        /// Generates RSA keys to be used for encrypting data where the private key is not
        /// encrypted on the server. (i.e. it should be stored securely in the same way you
        /// store the private key for an SSL cert)
        /// </summary>
        /// <returns></returns>
        Task GenerateRsaKeyList();

        /// <summary>
        /// Gets the current RSA Keys generated by <see cref="GenerateRsaKeyList"/>.
        /// </summary>
        /// <returns></returns>
        Task<RsaKeyListData> GetRsaKeyList();
        
        /// <summary>
        /// Checks if the nonce-hashed password matches the stored
        /// <see cref="LoginKeyData.HashPassword"/> (hashed with a <param name="nonce64">nonce</param>
        /// </summary>
        /// <param name="nonceHashedPassword64"></param>
        /// <param name="nonce64"></param>
        /// <returns></returns>
        Task TryPasswordKeyMatch(string nonceHashedPassword64, string nonce64);
    }
}