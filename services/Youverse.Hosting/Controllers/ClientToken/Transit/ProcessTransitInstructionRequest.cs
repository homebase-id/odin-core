using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Controllers.ClientToken.Transit;

public class ProcessTransitInstructionRequest
{
    public TargetDrive TargetDrive { get; set; }
}