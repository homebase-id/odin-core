#nullable enable
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.ClientToken.App.Commands;

public class GetUnprocessedCommandsRequest
{
    public TargetDrive TargetDrive { get; set; } = new();
    public string Cursor { get; set; } = "";
}