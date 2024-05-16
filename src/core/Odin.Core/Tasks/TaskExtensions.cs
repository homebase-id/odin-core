using System.Threading.Tasks;

namespace Odin.Core.Tasks;

public static class TaskExtensions
{
    /// <summary>
    /// Call this instead of Task.Wait() if you want want to deal with AggregateExceptions
    /// </summary>
    /// <param name="task"></param>
    public static void BlockingWait(this Task task)
    {
        task.GetAwaiter().GetResult();
    }
}
