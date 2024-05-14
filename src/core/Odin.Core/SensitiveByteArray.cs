using System;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;

namespace Odin.Core
{
    /// <summary>
    /// Use this class to store any "secret" byte[] in memory. The secret will
    /// be wiped when class is destroyed.
    /// </summary>
    [DebuggerDisplay("Key={string.Join(\"-\", _key)}")]
    public sealed class SensitiveByteArray: IDisposable, IGenericCloneable<SensitiveByteArray>
    {
        [JsonIgnore] private byte[] _key;

        private SensitiveByteArray()
        {
        }

        public SensitiveByteArray(byte[] data)
        {
            SetKey(data);
        }

        public SensitiveByteArray(string data64)
        {
            SetKey(Convert.FromBase64String(data64));
        }

        private SensitiveByteArray(SensitiveByteArray other)
        {
            if (other._key == null)
            {
                _key = null;
            }
            else
            {
                SetKey(other._key);
            }
        }

        public void Dispose()
        {
            Wipe();
        }

        public SensitiveByteArray Clone()
        {
            return new SensitiveByteArray(this);
        }

        public void Wipe()
        {
            if (_key != null)
            {
                ByteArrayUtil.WipeByteArray(_key);
            }
            _key = null;
        }

        public void SetKey(byte[] data)
        {
            if (data == null)
            {
                throw new OdinSystemException("Can't assign a null key");
            }
            if (data.Length < 1)
            {
                throw new OdinSystemException("Can't set an empty key");
            }

            Wipe();

            _key = new byte[data.Length];
            Array.Copy(data, _key, data.Length);
        }

        public byte[] GetKey()
        {
            if (_key == null)
            {
                throw new OdinSystemException("No key set");
            }
            return _key;
        }

        public bool IsEmpty()
        {
            return _key == null;
        }

        public bool IsSet()
        {
            return _key != null;
        }
    }
}