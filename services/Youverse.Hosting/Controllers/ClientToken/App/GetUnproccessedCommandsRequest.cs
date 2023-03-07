#nullable enable
using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Controllers.ClientToken.App;

public class GetUnproccessedCommandsRequest
{
    public TargetDrive TargetDrive { get; set; } = new();
    public string Cursor { get; set; } = "";
}