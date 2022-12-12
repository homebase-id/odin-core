using System;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Transit
{
    public class TransitPublicKey
    {
        [Obsolete]
        public Guid AppId { get; set; }

        public RsaPublicKeyData PublicKeyData { get; set; }

        public bool IsValid()
        {
            return this.PublicKeyData.IsValid();
        }
    }
}