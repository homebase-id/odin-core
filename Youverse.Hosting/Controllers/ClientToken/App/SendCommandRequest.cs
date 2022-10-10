#nullable enable
using Youverse.Core.Services.Apps.CommandMessaging;

namespace Youverse.Hosting.Controllers.ClientToken.App;

public class SendCommandRequest
{
    public CommandMessage Command { get; set; }
}