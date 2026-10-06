using System.Globalization;
using Odin.Core.Time;
using Odin.Services.Admin.Tenants;

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
        time.ToDateTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string ToCliTime(this UnixTimeUtc? time, string absent = "-") => time?.ToCliTime() ?? absent;

    public static string CreatedText(this TenantModel tenant) => tenant.Created.ToCliTime();

    public static string LastActivityText(this TenantModel tenant) => tenant.LastActivity.ToCliTime("never");
}
