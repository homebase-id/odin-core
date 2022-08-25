using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.Transit;

public class ProcessTransfersRequest
{
    public TargetDrive TargetDrive { get; set; }
}