using System;

namespace Odin.Services.Base.SharedTypes;

public class GetDrivesByTypeRequest
{
    public Guid DriveType { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}