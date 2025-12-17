using System;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class UpdateLocalMetadataContentRequestV2
{
    public byte[] Iv { get; init; }
    
    public Guid LocalVersionTag { get; init; }

    public string Content { get; init; }
}