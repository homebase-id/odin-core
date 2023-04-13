using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Controllers.ClientToken.Transit;

public class ProcessInboxRequest
{
    public TargetDrive TargetDrive { get; set; }
}