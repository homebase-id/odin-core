using System;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitPublicKey
    {
        public byte[] PublicKey { get; set; }
        public UInt64 Expiration { get; set; }
    }
}