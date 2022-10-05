using System;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class CreateGroupCommand : CommandBase
{
    public ChatGroup ChatGroup { get; set; }
}

public class SendReactionCommand : CommandBase
{
    public Guid MessageId { get; set; }
    public string ReactionCode { get; set; }
}