using System.Text.RegularExpressions;

namespace Youverse.Core;

public static class Validators
{
    const string ValidFilenamePattern = @"[A-Za-z0-9\.\-]";

    public static bool IsValidFilename(string filename)
    {
        return Regex.IsMatch(filename, ValidFilenamePattern, RegexOptions.IgnoreCase);
    }
}