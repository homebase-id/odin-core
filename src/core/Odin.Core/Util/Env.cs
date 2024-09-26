using System;
using System.Text.RegularExpressions;

namespace Odin.Core.Util;

#nullable enable

public static class Env
{
    static Env()
    {
        // Windows: HOME is not set by default, set it to USERPROFILE
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (home == null)
            {
                Environment.SetEnvironmentVariable("HOME", Environment.GetEnvironmentVariable("USERPROFILE"));
            }
        }
    }
    
    // Use this when you don't have access to IHostEnvironment
    public static bool IsDevelopment()
    {
        // Lifted from Microsoft.Extensions.Hosting.HostEnvironmentEnvExtensions
        return string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);
    }
    
    //
    
    public static string EnvironmentVariableEscape(string value)
    {
        return value
            .Replace(@"\", @"\\") // Escape backslashes first to avoid double escaping
            .Replace("'", @"\'"); // Escape single quotes
    }
    
    //
    
    // Rules for Environment Variable Names:
    // In Linux/macOS, valid environment variable names:
    // - Can consist of A-Z, a-z, 0-9, _
    // - Cannot start with a digit.            
    private static readonly Regex LinuxEnvVarPattern = new(@"\$(\{?[A-Za-z_][A-Za-z0-9_]*\}?)");
    public static string ExpandEnvironmentVariablesCrossPlatform(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            return Environment.ExpandEnvironmentVariables(name);
        }

        // Replace Linux-style $VARIABLE or ${VARIABLE} with %VARIABLE% for ExpandEnvironmentVariables
        var windowsStyleInput = LinuxEnvVarPattern.Replace(name, match =>
        {
            // Remove the curly braces if present (${VARIABLE} -> VARIABLE)
            var variableName = match.Value.Trim('$', '{', '}');
            return $"%{variableName}%";
        });

        return Environment.ExpandEnvironmentVariables(windowsStyleInput);
    }
}