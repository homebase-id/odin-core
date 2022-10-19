using System;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.Certificate;

public class DeleteLinkedFileRequest
{
    public TargetDrive TargetDrive { get; set; }
    public Guid GlobalTransitId { get; set; }
}