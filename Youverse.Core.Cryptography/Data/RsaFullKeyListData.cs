using System;
using System.Collections.Generic;

namespace Youverse.Core.Cryptography.Data
{
    // This class should be stored on the identity
    public class RsaFullKeyListData
    {
        public Guid Id { get; set; }
        public List<RsaFullKeyData> ListRSA { get; set; }// List.first is the current key, the rest are historic
        public int MaxKeys { get; set; } // At least 1. 
    }
}