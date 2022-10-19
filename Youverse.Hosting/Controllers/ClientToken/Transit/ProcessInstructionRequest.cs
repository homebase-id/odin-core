using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.Transit;

public class ProcessInstructionRequest
{
    public TargetDrive TargetDrive { get; set; }
}