using System;

namespace Youverse.Core.Cryptography
{
    public static class ArrayExtensions
    {
        public static SensitiveByteArray ToSensitiveByteArray(this Byte[] array)
        {
            return new SensitiveByteArray(array);
        }

        public static void WriteZeros(this Byte[] array)
        {
            ByteArrayUtil.WipeByteArray(array);
        }
        
        public static string ToBase64(this byte[] array)
        {
            //I know, I know, this extension method does not do much but it looks better when used :)
            return Convert.ToBase64String(array);
        }
    }
}