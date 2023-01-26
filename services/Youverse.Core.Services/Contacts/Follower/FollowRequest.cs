using System.Collections.Generic;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Contacts.Follower;

public class FollowRequest
{
    public string DotYouId { get; set; }
    public FollowerNotificationType NotificationType { get; set; }
    public IEnumerable<TargetDrive> Channels { get; set; }
}

public class UnfollowRequest
{
    public string DotYouId { get; set; }
}