using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Contacts.Follower;

/// <summary/>
public class FollowerDefinition
{
    public DotYouIdentity DotYouId { get; set; }

    public FollowerNotificationType NotificationType { get; set; }
    public IEnumerable<TargetDrive> Channels { get; set; }
}