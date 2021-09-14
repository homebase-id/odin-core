using System;
using System.Collections.Generic;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    // This class should be stored on the identity
    public class RsaKeyListData
    {
        //HACK: Id only used for storage layer
        public Guid Id { get; set; }
        public List<RsaKeyData> ListRSA { get; set; }// List.first is the current key, the rest are historic
        public int MaxKeys { get; set; } // At least 1. 
    }
}