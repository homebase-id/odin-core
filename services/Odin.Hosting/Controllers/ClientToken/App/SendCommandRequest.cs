#nullable enable
using Odin.Core.Services.Apps.CommandMessaging;
using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.ClientToken.App;

public class SendCommandRequest
{
    public CommandMessage Command { get; set; } = new();
    public TargetDrive TargetDrive { get; set; } = new();
}