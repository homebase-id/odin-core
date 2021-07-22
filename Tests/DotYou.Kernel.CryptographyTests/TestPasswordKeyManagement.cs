using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DotYou.Kernel.Cryptography;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NUnit.Framework;

namespace DotYou.Kernel.CryptographyTests
{
    public class TestPasswordKeyManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        [Test]
        public void CreateInitialPasswordKeyPass()
        {
            var np = NoncePackage.NewRandomNonce();
            var pr = new PasswordReply()
            {
                Nonce64 = np.Nonce64
            };

            pr.HashedPassword64 = Convert.ToBase64String(KeyDerivation.Pbkdf2("EnSø", Convert.FromBase64String(np.SaltPassword64), KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE));
            pr.NonceHashedPassword64 = Convert.ToBase64String(KeyDerivation.Pbkdf2(pr.HashedPassword64, Convert.FromBase64String(np.Nonce64), KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE));
            pr.KeK64 = Convert.ToBase64String(KeyDerivation.Pbkdf2("EnSø", Convert.FromBase64String(np.SaltKek64), KeyDerivationPrf.HMACSHA256, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE));

            var KeK = Convert.FromBase64String(pr.KeK64);

            PasswordKey pk = PasswordKeyManagement.SetInitialPassword(np, pr);



        }
    }
}
