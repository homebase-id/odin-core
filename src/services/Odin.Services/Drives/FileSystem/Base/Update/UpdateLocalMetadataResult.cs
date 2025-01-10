using System;
using System.Collections.Generic;

namespace Odin.Services.Drives.FileSystem.Base.Update;

public class UpdateLocalMetadataRequest
{
    public ExternalFileIdentifier File { get; set; }

    public string Content { get; set; }

    public List<Guid> Tags { get; set; }
}

public class UpdateLocalMetadataResult
{
    public Guid NewLocalVersionTag { get; set; }
}