using System;
using System.IO;
using System.Text;

namespace Odin.Core
{
    public static class ArrayExtensions
    {
        /*
         * Dont think this is needed
         *
         public static UnixTimeUtcSeconds ToUnixTimeUtcSeconds(this DateTime dateTime)
         {
             return new UnixTimeUtcSeconds(dateTime);
         }*/

        public static bool IsNullOrEmpty(this byte[] bytes)
        {
            return bytes == null || bytes.Length == 0;
        }

        public static string ToStringFromUtf8Bytes(this Byte[] bytes)
        {
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        public static SensitiveByteArray ToSensitiveByteArray(this Byte[] array)
        {
            return new SensitiveByteArray(array);
        }

        public static void Wipe(this Byte[] array)
        {
            ByteArrayUtil.WipeByteArray(array);
        }

        public static string ToBase64(this byte[] array)
        {
            //I know, I know, this extension method does not do much but it looks better when used :)
            return Convert.ToBase64String(array);
        }

        public static MemoryStream ToMemoryStream(this byte[] array, bool writable = false)
        {
            return new MemoryStream(array, writable);
        }

        public static string ToHexString(this byte[] array)
        {
            return Convert.ToHexString(array);
        }
    }
}