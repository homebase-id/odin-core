using Odin.Services.Base;

namespace Odin.Hosting.Controllers.Base.Drive.GroupReactions;

public class GetReactionsRequestRedux
{
    public FileIdentifier File { get; init; }
    public int Cursor { get; init; }
    public int MaxRecords { get; init; } = 100;
}