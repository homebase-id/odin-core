using System;

namespace DotYou.Types.Cryptography
{
    /// <summary>
    /// Reply from the client during authentication which includes the client's
    /// password hashed using the data from <see cref="ClientNoncePackage.SaltPassword64"/>
    /// </summary>
    public sealed class AuthenticationNonceReply:IPasswordReply
    {
        public string Nonce64 { get; set; }

        public string HashedPassword64 { get; set; }
        public string KeK64 { get; set; }

        public string NonceHashedPassword64 { get; set; }
        public UInt32 crc { get; set; }
        public string RsaEncrypted { get; set; }
    }
}