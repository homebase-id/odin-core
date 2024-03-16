using System.Collections.Generic;

namespace Odin.Services.Drives.Reactions;

public class AddGroupReactionResponse
{
    public List<RemoteAddDeleteReactionResponse> Responses { get; set; } = new ();
}

public class DeleteGroupReactionResponse
{
    public List<RemoteAddDeleteReactionResponse> Responses { get; set; } = new ();
}