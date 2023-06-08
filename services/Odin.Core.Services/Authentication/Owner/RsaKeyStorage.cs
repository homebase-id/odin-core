using System;
using System.Collections.Generic;
using Odin.Core.Cryptography.Data;

namespace Odin.Core.Services.Authentication.Owner
{
    class RsaKeyStorage
    {
        public Guid Id { get; set; }

        public List<RsaFullKeyData> Keys { get; set; }
    }
}