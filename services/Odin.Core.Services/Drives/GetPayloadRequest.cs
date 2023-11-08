using System;
using Odin.Core.Services.Drives.FileSystem.Base;

namespace Odin.Core.Services.Drives;

public abstract class GetPayloadRequestBase
{
    public FileChunk Chunk { get; set; }
    public string Key { get; set; }
}

public class GetPayloadRequest : GetPayloadRequestBase
{
    public ExternalFileIdentifier File { get; set; }
}

public class GetPayloadByGlobalTransitIdRequest : GetPayloadRequestBase
{
    public GlobalTransitIdFileIdentifier File { get; set; }
}

public class GetPayloadByUniqueIdRequest : GetPayloadRequestBase
{
    public Guid UniqueId { get; set; }
    public TargetDrive TargetDrive { get; set; }
}

public class GetFileHeaderByUniqueIdRequest
{
    public Guid UniqueId { get; set; }
    public TargetDrive TargetDrive { get; set; }
}