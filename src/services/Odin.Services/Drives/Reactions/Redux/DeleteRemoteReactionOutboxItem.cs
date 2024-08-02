using Odin.Core.Storage;
using Odin.Services.Base;

namespace Odin.Services.Drives.Reactions.Redux;

public class DeleteRemoteReactionOutboxItem
{
    public FileIdentifier File { get; init; }
    public string Reaction { get; init; }
}