using System;

namespace Odin.Core.Services.Authentication.Owner
{
    public sealed class ClientNoncePackage
    {
        public string SaltPassword64 { get; set; }
        public string SaltKek64 { get; set; }
        public string Nonce64 { get; set; }
        public string PublicPem { get; set; }
        public UInt32 CRC { get; set; }
    }
}