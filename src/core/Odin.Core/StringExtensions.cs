using System;

namespace Odin.Core
{
    public static class StringExtensions
    {
        public static byte[] ToUtf8ByteArray(this string str)
        {
            return System.Text.Encoding.UTF8.GetBytes(str);
        }

        public static byte[] FromBase64(this string str)
        {
            return Convert.FromBase64String(str);
        }
    }
}