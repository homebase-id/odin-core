namespace Odin.Services.Configuration;

/// <summary>
/// Indicates version information the current release (data structure version, code version, etc.)
/// </summary>
public static class ReleaseVersionInfo
{
    public const int DataVersionNumber = 2;

    //TODO: need to automate changing this per build
    public const string BuildVersion = "b41a19a1-0108-48e5-8233-ac82623c7a07";
}