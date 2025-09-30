using System.Threading;

namespace Odin.Core.Logging.Caller;

public interface ICallerLogContext
{
    string Caller { get; set; }
}

//

public class CallerLogContext : ICallerLogContext
{
    private static readonly AsyncLocal<string> _caller = new();

    public string Caller
    {
        get => _caller.Value ?? (_caller.Value = "no-caller");
        set => _caller.Value = value;
    }
}

