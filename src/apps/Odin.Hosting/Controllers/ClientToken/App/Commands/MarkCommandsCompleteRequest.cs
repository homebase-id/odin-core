#nullable enable
using System;
using System.Collections.Generic;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.ClientToken.App.Commands;

public class MarkCommandsCompleteRequest
{
    public TargetDrive TargetDrive { get; set; } = new();
    public IEnumerable<Guid> CommandIdList { get; set; } = Array.Empty<Guid>();
}