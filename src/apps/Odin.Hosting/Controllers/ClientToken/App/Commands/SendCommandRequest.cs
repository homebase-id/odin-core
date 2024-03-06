#nullable enable
using Odin.Services.Apps.CommandMessaging;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.ClientToken.App.Commands;

public class SendCommandRequest
{
    public CommandMessage Command { get; set; } = new();
    public TargetDrive TargetDrive { get; set; } = new();
}