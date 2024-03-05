using System.Text.RegularExpressions;

namespace Odin.Core;

public static class Validators
{
    const string AlnumPattern = @"^[A-Za-z0-9]+$";
    
    // isAlnum is the C name for checking for a-zA-Z0-9
    public static bool IsAlnum(string data)
    {
        return Regex.IsMatch(data, AlnumPattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);

    }
}