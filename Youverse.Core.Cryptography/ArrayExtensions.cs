using System;

namespace Youverse.Core.Cryptography
{
    public static class ArrayExtensions
    {
        public static SensitiveByteArray ToSensitiveByteArray(this Byte[] array)
        {
            return new SensitiveByteArray(array);
        }
    }
}