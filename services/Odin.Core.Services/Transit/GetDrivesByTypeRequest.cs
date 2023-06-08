using System;

namespace Odin.Core.Services.Transit;

public class GetDrivesByTypeRequest
{
    public Guid DriveType { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}