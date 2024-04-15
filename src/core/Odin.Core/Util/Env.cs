using System;

namespace Odin.Core.Util;

public class Env
{
    // Use this when you don't have access to IHostEnvironment
    public static bool IsDevelopment()
    {
        // Lifted from Microsoft.Extensions.Hosting.HostEnvironmentEnvExtensions
        return string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);
    }
}