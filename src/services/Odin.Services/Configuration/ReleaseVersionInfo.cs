namespace Odin.Services.Configuration;

/// <summary>
/// Indicates version information the current release (data structure version, code version, etc.)
/// </summary>
public static class ReleaseVersionInfo
{
    public const int DataVersionNumber = 2;

    //TODO: need to automate changing this per build
    public const string BuildVersion = "4d4b69c8-fb54-4c25-966d-e7f989fc0950";
}