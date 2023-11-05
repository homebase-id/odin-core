using Quartz;

namespace Odin.Test.Helpers.Quarz;

public class NonExclusiveJobSimulation : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500));
    }
}