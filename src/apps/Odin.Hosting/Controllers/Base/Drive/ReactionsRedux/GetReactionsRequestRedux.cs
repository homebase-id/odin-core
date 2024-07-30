using Odin.Services.Base;

namespace Odin.Hosting.Controllers.Base.Drive.ReactionsRedux;

public class GetReactionsRequestRedux
{
    public FileIdentifier File { get; set; }
    public int Cursor { get; set; }

    public int MaxRecords { get; set; }
}