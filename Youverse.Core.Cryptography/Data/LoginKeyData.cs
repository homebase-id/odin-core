using System;

namespace Youverse.Core.Cryptography.Data
{
    public class LoginKeyData
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

        /// <summary>
        /// This is the DeK encrypted with the KeK. You'll derive the KeK from the 
        /// LoginTokenData when the client and server halves meet.
        /// </summary>
        public byte[] XorEncryptedDek { get; set; }

        /// <summary>
        /// The value which must be matched by the admin verification service to establish the running thing
        /// </summary>
        public byte[] VerificationValue { get; set; }

        /// <summary>
        /// Encrypted with Admin Dek
        /// </summary>
        public byte[] EncryptedVerificationValue { get; set; }

        /// <summary>
        /// The initialization vector for the encryption of the <see cref="VerificationValue"/>
        /// </summary>
        public byte[] VerificationIv { get; set; }

    }
}
