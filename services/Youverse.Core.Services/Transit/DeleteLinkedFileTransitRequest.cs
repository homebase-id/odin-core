using System;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit;

public class DeleteLinkedFileTransitRequest
{
    public TargetDrive TargetDrive { get; set; }
    public Guid GlobalTransitId { get; set; }
}