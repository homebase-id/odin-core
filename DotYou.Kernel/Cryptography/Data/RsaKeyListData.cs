using System.Collections.Generic;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    // This class should be stored on the identity
    public class RsaKeyListData
    {
        public LinkedList<RsaKeyData> listRSA;  // List.first is the current key, the rest are historic
        public int maxKeys; // At least 1. 
    }
}