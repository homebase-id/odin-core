using System.Net;
using Odin.Core.Identity;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class RemoteAddDeleteReactionResponse
{
    public OdinId Recipient { get; set; }
    public HttpStatusCode RemoteHttpStatusCode { get; set; }
}