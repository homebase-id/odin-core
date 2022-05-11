using System;
using System.Security.Cryptography;

namespace Youverse.Core.Cryptography
{
    public static class ArrayExtensions
    {
        public static string ToStringFromUTF8Bytes(this Byte[] bytes)
        {
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        
        public static SensitiveByteArray ToSensitiveByteArray(this Byte[] array)
        {
            return new SensitiveByteArray(array);
        }

        
        public static string ToBase64(this byte[] array)
        {
            //I know, I know, this extension method does not do much but it looks better when used :)
            return Convert.ToBase64String(array);
        }
    }
}