using System.Collections.Generic;

namespace Odin.Core.Cryptography.Data
{
    // This class should be stored on the identity
    public class EccFullKeyListData
    {
        public List<EccFullKeyData> ListEcc { get; set; }// List.first is the current key, the rest are historic
        public int MaxKeys { get; set; } // At least 1. 
    }
}
