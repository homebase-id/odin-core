using System;
using System.IO;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Cryptography;
using DotYou.Types;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    public class OwnerSecretService : DotYouServiceBase, IOwnerSecretService
    {
        private const string STORAGE = "Provisioning";
        private const string PWD_STORAGE = "k3";

        public OwnerSecretService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
        }

        /// <summary>
        /// Generates two 16 byte crypto-random numbers used for salting passwords
        /// </summary>
        /// <returns></returns>
        public Task<NoncePackage> GenerateNewSalts()
        {
            var nonce = NoncePackage.NewRandomNonce();
            WithTenantStorage<NoncePackage>(STORAGE, s => s.Save(nonce));
            return Task.FromResult(nonce);
        }

        public async Task SetNewPassword(PasswordReply reply)
        {
            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));

            var originalNoncePackage = await WithTenantStorageReturnSingle<NoncePackage>(STORAGE, s => s.Get(originalNoncePackageKey));

            var b1 = Convert.FromBase64String(reply.NonceHashedPassword64);
            var b2 = KeyDerivation.Pbkdf2(
                reply.HashedPassword64,
                Convert.FromBase64String(reply.Nonce64),
                KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS,
                CryptographyConstants.HASH_SIZE);

            // var hashNoncePassword64 = await _js.InvokeAsync<string>("wrapPbkdf2HmacSha256", hashedPasswordBytes, saltNonceBytes, 100000, 16);

            
            // var hashedPassword64 = await _js.InvokeAsync<string>("wrapPbkdf2HmacSha256", passwordBytes, saltPasswordBytes, 100000, 16);
            // var hashedPasswordBytes = Convert.FromBase64String(hashedPassword64);
            
            if (YFByteArray.EquiByteArrayCompare(b1, b2) == false)
            {
                throw new InvalidDataException("Invalid payload");
            }

            var pk = new PasswordKey()
            {
                HashPassword = Convert.FromBase64String(reply.HashedPassword64),
                SaltPassword = Convert.FromBase64String(originalNoncePackage.SaltPassword64),
                SaltKek = Convert.FromBase64String(originalNoncePackage.SaltKek64)
            };

            //TODO: revisit how and when we generate data encryption keys
            //reply.KeK64
            // RSACryptoServiceProvider rsaGenKeys = new RSACryptoServiceProvider(2048);
            // PublicKey = rsaGenKeys.ToXmlString(false);
            // EncryptedPrivateKey = rsaGenKeys.ToXmlString(true);
            //
            // // Server Encrypts the private key with the KEK and throws the KEK away.
            // byte[] encrypted = YFRijndaelWrap.EncryptStringToBytes(EncryptedPrivateKey, reply.KeK64, SaltPassword);
            // EncryptedPrivateKey = Convert.ToBase64String(encrypted);
            // YFByteArray.WipeByteArray(KeyEncryptionKey);

            WithTenantStorage<PasswordKey>(PWD_STORAGE, s => s.Save(pk));
            
            //delete the temporary salts
            WithTenantStorage<NoncePackage>(STORAGE, s => s.Delete(originalNoncePackageKey));
        }

        public async Task<SaltsPackage> GetStoredSalts()
        {
            var pk = await WithTenantStorageReturnSingle<PasswordKey>(PWD_STORAGE, s => s.Get(PasswordKey.Key));

            if (null == pk)
            {
                throw new InvalidDataException("Secrets configuration invalid");
            }

            return new SaltsPackage()
            {
                SaltKek64 = Convert.ToBase64String(pk.SaltKek),
                SaltPassword64 = Convert.ToBase64String(pk.SaltPassword)
            };
        }

        public async Task<bool> IsPasswordKeyMatch(string nonceHashedPassword64, string nonce64)
        {
            var pk = await WithTenantStorageReturnSingle<PasswordKey>(PWD_STORAGE, s => s.Get(PasswordKey.Key));
            var noncePasswordBytes = Convert.FromBase64String(nonceHashedPassword64);
            
            var nonceHashedPassword = KeyDerivation.Pbkdf2(
                Convert.ToBase64String(pk.HashPassword),
                Convert.FromBase64String(nonce64),
                KeyDerivationPrf.HMACSHA256,
                CryptographyConstants.ITERATIONS,
                CryptographyConstants.HASH_SIZE);

            bool match = YFByteArray.EquiByteArrayCompare(noncePasswordBytes, nonceHashedPassword);
            return match;
        }
    }
}