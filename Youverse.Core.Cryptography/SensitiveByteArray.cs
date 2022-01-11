﻿using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Youverse.Core.Cryptography
{
    /// <summary>
    /// Use this class to store any "secret" byte[] in memory. The secret will
    /// be wiped when class is destroyed. 
    /// TODO consider moving to real memory outside GC space, so it can't get 
    /// moved around. 
    /// TODO write tests.
    /// </summary>
    [DebuggerDisplay("Key={string.Join(\"-\", _key)}")]
    public sealed class SensitiveByteArray: IDisposable
    {
        // TODO Move this to secure memory
        [JsonIgnore] private byte[] _key;
        // TODO - test to make sure it doesnt get saved

        public SensitiveByteArray()
        {
            _key = null;
        }

        public SensitiveByteArray(byte[] data)
        {
            SetKey(data);
        }

        public SensitiveByteArray(string data64)
        {
            SetKey(Convert.FromBase64String(data64));
        }

        ~SensitiveByteArray()
        {
            //Wipe();
        }

        public void Dispose()
        {
            this.Wipe();
        }

        public void Wipe()
        {
            if (_key != null)
                ByteArrayUtil.WipeByteArray(_key);

            _key = null;
        }


        public void SetKey(byte[] data)
        {
            if (_key != null)
                throw new Exception("Can't set a key which is already set");
            _key = data;
        }

        public byte[] GetKey()
        {
            if (_key == null)
                throw new Exception("No key set");
            return _key;
        }

        public bool IsEmpty()
        {
            return (_key == null);
        }

        public bool IsSet()
        {
            return (_key != null);
        }
    }
}