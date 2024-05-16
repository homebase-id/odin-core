using System.Threading.Tasks;

namespace Odin.Core.Tasks;

public static class TaskExtensions
{
    /// <summary>
    /// Call Task.BlockingWait() instead of Task.Wait() if you don't want to deal with AggregateExceptions
    /// </summary>
    /// <param name="task"></param>
    public static void BlockingWait(this Task task)
    {
        task.GetAwaiter().GetResult();
    }
}
