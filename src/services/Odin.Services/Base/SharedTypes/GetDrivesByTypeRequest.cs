using System;

namespace Odin.Services.Base.SharedTypes;

public class GetDrivesByTypeRequest
{
    public Guid DriveType { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}


public class RemoteFileExistsByUidAndVersionTagRequest  
{
    public Guid DriveId { get; set; }
    public Guid UniqueId { get; set; }
    public Guid VersionTag { get; set; }
}