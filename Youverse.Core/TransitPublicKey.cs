using System;
using Youverse.Core.Identity;

namespace Youverse.Core
{
    public class TransitPublicKey
    {
        public Guid Id { get; set; }
        public byte[] PublicKey { get; set; }
        public UInt64 Expiration { get; set; }
        public UInt32 Crc { get; set; }

        public bool IsExpired()
        {
            var now = DateTimeExtensions.UnixTimeSeconds();
            return now > this.Expiration;
        }

        public bool IsValid()
        {
            return this.IsExpired() == false && PublicKey.Length > 0;
        }
    }
}