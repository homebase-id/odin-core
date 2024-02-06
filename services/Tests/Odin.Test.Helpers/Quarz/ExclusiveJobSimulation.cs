using Odin.Core.Services.Quartz;
using Quartz;

namespace Odin.Test.Helpers.Quarz;

public class ExclusiveJobSimulation : IExclusiveJob
{
    public string JobId { get; } = Guid.NewGuid().ToString();
    public IJobState State => _state;
    public bool IsDone => _isDone;

    private readonly JobState _state = new();
    private volatile bool _isDone;

    public async Task Execute(IJobExecutionContext context)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        _isDone = true;
    }

}