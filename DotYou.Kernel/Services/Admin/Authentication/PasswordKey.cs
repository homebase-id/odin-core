using System;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    public class PasswordKey
    {
                                
        public static Guid Key => Guid.Parse("11111111-1111-1111-1111-111111111111");

        /// <summary>
        /// There should only be one password key stored
        /// </summary>
        public Guid Id => Key;

        /// <summary>
        /// The 16 byte salt used for the password
        /// </summary>
        public byte[] SaltPassword { get; set; }

        /// <summary>
        /// The 16 byte salt used for the KEK
        /// </summary>
        public byte[] SaltKek { get; set; }

        /// <summary>
        /// The Hashed password with SaltPassword, never compared directly with anything
        /// </summary>
        public byte[] HashPassword { get; set; }


        public byte[] XorEncryptedDek { get; set; }
    }
}