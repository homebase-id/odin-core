namespace Odin.Core.Util
{
    public static class ValidationUtil
    {
        public static bool IsNullEmptyOrWhitespace(string value)
        {
            return string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(value);
        }
    }
}