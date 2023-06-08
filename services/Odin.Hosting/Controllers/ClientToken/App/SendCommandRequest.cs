#nullable enable
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Controllers.ClientToken.App;

public class SendCommandRequest
{
    public CommandMessage Command { get; set; } = new();
    public TargetDrive TargetDrive { get; set; } = new();
}