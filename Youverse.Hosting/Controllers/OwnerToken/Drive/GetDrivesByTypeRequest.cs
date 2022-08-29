using System;
using Youverse.Core;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive;

public class GetDrivesByTypeRequest
{
    public ByteArrayId DriveType { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}