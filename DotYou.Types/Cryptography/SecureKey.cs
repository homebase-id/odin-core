using System;
using System.Text.Json.Serialization;

namespace DotYou.Kernel.Cryptography
{
    /// <summary>
    /// Use this class to store any "secret" byte[] in memory. The secret will
    /// be wiped when class is destroyed. 
    /// TODO consider moving to real memory outside GC space, so it can't get 
    /// moved around. 
    /// TODO write tests.
    /// </summary>
    public sealed class SecureKey
    {
        // TODO Move this to secure memory
        [JsonIgnore]
        private byte[] key;
        // TODO - test to make sure it doesnt get saved

        public SecureKey()
        {
            key = null;
        }

        public SecureKey(byte[] data)
        {
            SetKey(data);
        }

        ~SecureKey()
        {
            if (key != null)
                YFByteArray.WipeByteArray(key);
        }

        public void SetKey(byte[] data)
        {
            if (key != null)
                throw new Exception("Can't set a key which is already set");
            key = data;
        }

        public byte[] GetKey()
        {
            if (key == null)
                throw new Exception("No key set");
            return key;
        }

        public bool IsEmpty()
        {
            return (key == null);
        }
        public bool IsSet()
        {
            return (key != null);
        }
    }
}
