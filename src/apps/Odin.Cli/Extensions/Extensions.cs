using System.Globalization;
using Odin.Core.Time;

namespace Odin.Cli.Extensions;

public static class Extensions
{
    public static string HumanReadableBytes(this long bytes)
    {
        string[] sizeSuffixes = { "Bi", "Ki", "Mi", "Gi", "Ti", "Pi", "Ei", "Zi", "Yi" };

        if (bytes == 0)
        {
            return "0" + sizeSuffixes[0];
        }

        var magnitudeIndex = (int)(Math.Log(bytes, 1024));
        var adjustedSize = (decimal)bytes / (1L << (magnitudeIndex * 10));

        return $"{adjustedSize.ToString("N1", CultureInfo.InvariantCulture)}{sizeSuffixes[magnitudeIndex]}";
    }

    /// <summary>A time as every command shows it: UTC, so hosts in different regions read alike.</summary>
    public static string ToCliTime(this UnixTimeUtc time) =>
        time.ToDateTimeOffset().UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string ToCliTime(this UnixTimeUtc? time, string absent = "-") => time?.ToCliTime() ?? absent;
}
