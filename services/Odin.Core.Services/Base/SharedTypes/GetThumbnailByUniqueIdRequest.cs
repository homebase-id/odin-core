using System;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Base.SharedTypes;

public class GetThumbnailByUniqueIdRequest : GetThumbnailRequestBase
{
    public Guid ClientUniqueId { get; set; }

    public TargetDrive TargetDrive { get; set; }
}