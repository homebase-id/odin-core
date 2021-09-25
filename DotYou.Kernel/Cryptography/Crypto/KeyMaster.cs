using System;

namespace DotYou.Kernel.Cryptography
{
    public class KeyMasterBase
    {
        protected byte[] key;

        public virtual byte[] GetKey(byte[] xor) { return null;  }  // I want to make this = NULL like in C++

        public KeyMasterBase()
        {
            key = null;
        }

        ~KeyMasterBase()
        {
            if (key != null)
                YFByteArray.WipeByteArray(key);
        }
    }

    public class MasterDek : KeyMasterBase
    {
        public override byte[] GetKey(byte[] xor)
        {
            key = Guid.NewGuid().ToByteArray();
 
            return key;
        }

    }
}
