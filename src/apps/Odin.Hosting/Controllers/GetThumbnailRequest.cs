using Odin.Core.Identity;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Hosting.Controllers;

public class TransitGetThumbRequest : GetThumbnailRequest
{
    public OdinId OdinId { get; set; }
}

public class TransitGetPayloadRequest : GetPayloadRequest
{  
    public OdinId OdinId { get; set; }
}

public class TransitExternalFileIdentifier
{
    public OdinId OdinId { get; set; }

    public ExternalFileIdentifier File { get; set; }

    public FileChunk Chunk { get; set; }
}

public class TransitGetDrivesByTypeRequest : GetDrivesByTypeRequest
{
    public OdinId OdinId { get; set; }
}

public class TransitGetSecurityContextRequest
{
    public OdinId OdinId { get; set; }
}