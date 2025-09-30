using Odin.Core.Cryptography.Data;
using Odin.Core.Time;

namespace Odin.Core.Cryptography.Login
{
    public class PasswordData
    {
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
        /// This is the DeK (encrypted with the KeK). You'll derive the KeK from the 
        /// LoginTokenData when the client and server halves meet. The KeK is sent
        /// ECC encrypted from the client to the host.
        /// </summary>
        public SymmetricKeyEncryptedAes KekEncryptedMasterKey { get; set; }
        
        /// <summary>
        /// The Connection Key used to decrypt the shared secret on connection info and the RSA online key for connection request
        /// </summary>
        public SymmetricKeyEncryptedAes MasterKeyEncryptedConnectionKey { get; set; }

        /// <summary>
        /// When the password was last updated
        /// </summary>
        public UnixTimeUtc? Updated { get; set; }
    }
}
