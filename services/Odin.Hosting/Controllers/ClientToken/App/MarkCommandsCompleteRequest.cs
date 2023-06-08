#nullable enable
using System;
using System.Collections.Generic;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Controllers.ClientToken.App;

public class MarkCommandsCompleteRequest
{
    public TargetDrive TargetDrive { get; set; } = new();
    public IEnumerable<Guid> CommandIdList { get; set; } = Array.Empty<Guid>();
}