using System;
using System.IO;
using Odin.Services.Configuration;

#nullable enable

namespace Odin.Services.Drives.FileSystem.Base;

public class SystemPathManager
{
    public readonly string SystemDataRootPath;

    public SystemPathManager(OdinConfiguration config)
    {
        SystemDataRootPath = config.Host.SystemDataRootPath;
        ArgumentException.ThrowIfNullOrEmpty(nameof(SystemDataRootPath));
    }

    public string GetSysDatabasePath()
    {
        return Path.Combine(SystemDataRootPath, "sys.db");
    }
}