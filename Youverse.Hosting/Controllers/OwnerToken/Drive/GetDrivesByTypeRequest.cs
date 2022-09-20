using System;
using Youverse.Core;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive;

public class GetDrivesByTypeRequest
{
    public Guid DriveType { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}