namespace Odin.Services.Configuration;

/// <summary>
/// Indicates version information the current release (data structure version, code version, etc.)
/// </summary>
public static class ReleaseVersionInfo
{
    public const int DataVersionNumber = 2;

    //TODO: need to automate changing this per build
    public const string BuildVersion = "86c45bb5-4f76-4ee2-b2e9-a161ffc90f85";
}