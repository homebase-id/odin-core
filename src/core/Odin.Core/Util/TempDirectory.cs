using System;
using System.IO;

namespace Odin.Core.Util;

public static class TempDirectory
{
    public static string Create()
    {
        var tempPath = Path.GetTempPath();
        var tempDirectory = Path.Combine(tempPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }    
}
