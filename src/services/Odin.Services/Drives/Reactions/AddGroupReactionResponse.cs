using System.Collections.Generic;

namespace Odin.Services.Drives.Reactions;

public class AddGroupReactionResponse
{
    public List<AddDeleteRemoteReactionResponse> Responses { get; set; } = new ();
}

public class DeleteGroupReactionResponse
{
    public List<AddDeleteRemoteReactionResponse> Responses { get; set; } = new ();
}