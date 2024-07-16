using System;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.APIv2.Drive.Files;

public class GetFileRequestV2
{
    public Guid FileId { get; set; }
    public TargetDrive TargetDrive { get; set; }
    public FileIdType FileIdType { get; set; }
}