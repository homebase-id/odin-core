#nullable enable
using System;
using System.Collections.Generic;
using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.ClientToken.App;

public class MarkCommandsCompleteRequest
{
    public TargetDrive TargetDrive { get; set; } = new();
    public IEnumerable<Guid> CommandIdList { get; set; } = Array.Empty<Guid>();
}