using System;
using System.Collections.Generic;

namespace Odin.Services.Drives.FileSystem.Base.Update;

public class UpdateLocalMetadataTagsRequest
{
    public Guid LocalVersionTag { get; init; }
    
    public ExternalFileIdentifier File { get; init; }

    public List<Guid> Tags { get; init; }
}

public class UpdateLocalMetadataContentRequest
{
    public Guid LocalVersionTag { get; init; }

    public ExternalFileIdentifier File { get; init; }

    public string Content { get; init; }
}


public class UpdateLocalMetadataResult
{
    public Guid NewLocalVersionTag { get; init; }
}