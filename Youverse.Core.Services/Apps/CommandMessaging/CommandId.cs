using System;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Apps.CommandMessaging;

public class CommandId
{
    public Guid Id { get; set; }
    public TargetDrive TargetDrive { get; set; }
}