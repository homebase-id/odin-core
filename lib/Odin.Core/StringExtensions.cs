namespace Odin.Core
{
    public static class StringExtensions
    {
        public static byte[] ToUtf8ByteArray(this string str)
        {
            return System.Text.Encoding.UTF8.GetBytes(str);
        }
    }
}