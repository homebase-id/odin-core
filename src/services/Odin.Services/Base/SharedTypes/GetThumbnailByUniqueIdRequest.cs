using System;
using Odin.Services.Drives;

namespace Odin.Services.Base.SharedTypes;

public class GetThumbnailByUniqueIdRequest : GetThumbnailRequestBase
{
    public Guid ClientUniqueId { get; set; }

    public TargetDrive TargetDrive { get; set; }
}