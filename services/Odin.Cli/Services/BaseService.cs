namespace Odin.Cli.Services;

public interface IBaseService
{
    bool Verbose { get; set; }
}

public abstract class BaseService : IBaseService
{
    public bool Verbose { get; set; } = false;
}
