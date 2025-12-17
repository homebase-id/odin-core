using System;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class DeletePayloadRequestV2 
{
    public string Key { get; set; }

    public Guid? VersionTag { get; set; }
}