using System;

namespace Youverse.Core
{
    public static class ArrayExtensions
    {
        public static string ToStringFromUtf8Bytes(this Byte[] bytes)
        {
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}