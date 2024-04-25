using System;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Odin.Core.Serialization;

namespace Odin.Core
{
    /// <summary>
    /// Use this class to store any "secret" byte[] in memory. The secret will
    /// be wiped when class is destroyed. 
    /// TODO consider moving to real memory outside GC space, so it can't get 
    /// moved around. 
    /// TODO write tests.
    /// </summary>
    [DebuggerDisplay("Key={string.Join(\"-\", _key)}")]
    public sealed class SensitiveByteArray: IDisposable, IGenericCloneable<SensitiveByteArray>
    {
        // TODO Move this to secure memory
        [JsonIgnore] private byte[] _key;

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

        public SensitiveByteArray(SensitiveByteArray other)
        {
            if (other._key == null)
            {
                _key = null;
            }
            else
            {
                _key = new byte[other._key.Length];
                Array.Copy(other._key, _key, _key.Length);
            }
        }

        public void Dispose()
        {
            this.Wipe();
            GC.SuppressFinalize(this);
        }

        public SensitiveByteArray Clone()
        {
            return new SensitiveByteArray(this);
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
            if (data == null)
                throw new Exception("Can't assign a null key");
            if (data.Length < 1)
                throw new Exception("Can't set an empty key");
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