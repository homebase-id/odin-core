using System;
using System.Collections.Generic;

namespace Odin.Core.Services.Configuration;

public class OwnerAppSettings
{
    public static readonly Guid ConfigKey = Guid.Parse("2b6f6f80-d1f4-4153-8ec6-3e6c98f99de3");

    public static OwnerAppSettings Default { get; } = new()
    {
        Settings = new Dictionary<string, string>()
    };

    public Dictionary<string, string> Settings { get; set; }
}