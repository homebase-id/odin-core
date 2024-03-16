using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive;

public class AddReactionRequest
{
    public ExternalFileIdentifier File { get; set; }
    public string Reaction { get; set; }
}