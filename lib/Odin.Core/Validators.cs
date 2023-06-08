using System.Text.RegularExpressions;

namespace Odin.Core;

public static class Validators
{
    const string ValidFilenamePattern = @"^[a-zA-Z0-9](?:[a-zA-Z0-9 ._-]*[a-zA-Z0-9])?\.[a-zA-Z0-9_-]+$";
    const string AlnumPattern = @"^[A-Za-z0-9]+$";

    public static bool IsValidFilename(string filename)
    {
        return Regex.IsMatch(filename, ValidFilenamePattern, RegexOptions.IgnoreCase);
    }

    // isAlnum is the C name for checking for a-zA-Z0-9
    public static bool IsAlnum(string data)
    {
        return Regex.IsMatch(data, AlnumPattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);

    }
}