#nullable enable
using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.ClientToken.App.Commands;

public class GetUnproccessedCommandsRequest
{
    public TargetDrive TargetDrive { get; set; } = new();
    public string Cursor { get; set; } = "";
}