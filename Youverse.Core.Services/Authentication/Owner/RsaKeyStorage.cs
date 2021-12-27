using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authentication.Owner
{
    class RsaKeyStorage
    {
        public Guid Id { get; set; }

        public List<RsaFullKeyData> Keys { get; set; }
    }
}