using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Core.Tasks;

public interface IForgottenTasks
{
    void Add(Task task);
    Task WhenAll();
}

public class ForgottenTasks : IForgottenTasks
{
    private readonly List<Task> _tasks = [];
    private readonly Lock _mutex = new ();

    public void Add(Task task)
    {
        lock (_mutex)
        {
            _tasks.RemoveAll(t => t.IsCompleted);
            _tasks.Add(task);
        }
    }

    public Task WhenAll()
    {
        Task[] tasksToAwait;
        lock (_mutex)
        {
            _tasks.RemoveAll(t => t.IsCompleted);
            tasksToAwait = _tasks.ToArray();
        }

        return Task.WhenAll(tasksToAwait);
    }
}
