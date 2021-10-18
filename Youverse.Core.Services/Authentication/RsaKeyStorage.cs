using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authentication
{
    class RsaKeyStorage
    {
        public Guid Id { get; set; }

        public List<RsaKeyData> Keys { get; set; }
    }
}