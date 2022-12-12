#nullable enable
using System;
using System.Collections.Generic;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.App;

public class MarkCommandsCompleteRequest
{
    public TargetDrive TargetDrive { get; set; }
    public IEnumerable<Guid> CommandIdList { get; set; }
}