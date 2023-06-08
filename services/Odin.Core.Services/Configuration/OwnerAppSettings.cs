using System.Collections.Generic;

namespace Odin.Core.Services.Configuration;

public class OwnerAppSettings
{
    public static readonly GuidId ConfigKey = GuidId.FromString("owner_app_settings");

    public static OwnerAppSettings Default { get; } = new()
    {
        Settings = new Dictionary<string, string>()
    };

    public Dictionary<string, string> Settings { get; set; }
}