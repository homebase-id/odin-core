using System;

namespace Odin.Services.Authentication.Owner
{
    public sealed class ClientNoncePackage
    {
        public string SaltPassword64 { get; set; }
        public string SaltKek64 { get; set; }
        public string Nonce64 { get; set; }
        public string PublicJwk { get; set; }
        public UInt32 CRC { get; set; }
    }
}