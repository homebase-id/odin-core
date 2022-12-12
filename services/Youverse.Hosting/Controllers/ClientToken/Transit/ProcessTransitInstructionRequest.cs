using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.Transit;

public class ProcessTransitInstructionRequest
{
    public TargetDrive TargetDrive { get; set; }
}