namespace DotYou.Types
{
    public static class ValidationUtil
    {
        public static bool HasNonWhitespaceValue(string value)
        {
            return string.IsNullOrEmpty(value) == false && string.IsNullOrWhiteSpace(value) == false;
        }
    }
}