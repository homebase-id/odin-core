using System;

namespace Odin.Core.Cryptography.Login
{

    public class PasswordReply
    {
        public string Nonce64 { get; set; }

        public string NonceHashedPassword64 { get; set; }
        
        public UInt32 crc { get; set; }
        public string GcmEncrypted64 { get; set; }
        public string PublicKeyJwk { get; set; }

        /// <summary>
        /// The token given during the provisioning process used to
        /// allow the caller to set the password the firs time
        /// </summary>
        public Guid? FirstRunToken { get; set; }
    }
}