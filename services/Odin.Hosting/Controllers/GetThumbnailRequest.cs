using Odin.Core.Services.Base.SharedTypes;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Peer;

namespace Odin.Hosting.Controllers;

public class TransitGetThumbRequest : GetThumbnailRequest
{
    public string OdinId { get; set; }
}

public class TransitGetPayloadRequest : GetPayloadRequest
{  
    public string OdinId { get; set; }
}

public class TransitExternalFileIdentifier
{
    public string OdinId { get; set; }

    public ExternalFileIdentifier File { get; set; }

    public FileChunk Chunk { get; set; }
}

public class TransitGetDrivesByTypeRequest : GetDrivesByTypeRequest
{
    public string OdinId { get; set; }
}

public class TransitGetSecurityContextRequest
{
    public string OdinId { get; set; }
}