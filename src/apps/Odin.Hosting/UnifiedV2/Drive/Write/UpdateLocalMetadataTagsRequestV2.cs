using System;
using System.Collections.Generic;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class UpdateLocalMetadataTagsRequestV2
{
    public Guid LocalVersionTag { get; init; }
    public List<Guid> Tags { get; init; }
}