using System;

namespace Youverse.Core.Cryptography
{
    /// <summary>
    /// Holds the values when the client creates a new Digital Identity
    /// </summary>
    public interface IPasswordReply
    {
        public string Nonce64 { get; set; }

        public string NonceHashedPassword64 { get; set; }
        
        public UInt32 crc { get; set; }
        public string RsaEncrypted { get; set; }
        
        /// <summary>
        /// The token given during the provisioning process used to
        /// allow the caller to set the password the firs time
        /// </summary>
        public Guid? FirstRunToken { get; set; }
    }

    public class PasswordReply: IPasswordReply
    {
        public string Nonce64 { get; set; }

        public string NonceHashedPassword64 { get; set; }
        
        public UInt32 crc { get; set; }
        public string RsaEncrypted { get; set; }
        public Guid? FirstRunToken { get; set; }
    }
}