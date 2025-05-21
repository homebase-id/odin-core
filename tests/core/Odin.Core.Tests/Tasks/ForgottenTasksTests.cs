using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Tasks;

namespace Odin.Core.Tests.Tasks;

public class ForgottenTasksTests
{
    [Test]
    public async Task ItShouldAddAndAwait()
    {
        var forgottenTasks = new ForgottenTasks();
        var task1 = Task.Delay(100);
        var task2 = Task.Delay(200);
        var task3 = Task.Delay(300);

        forgottenTasks.Add(task1);
        forgottenTasks.Add(task2);
        forgottenTasks.Add(task3);

        await forgottenTasks.WhenAll();

        Assert.That(task1.IsCompleted);
        Assert.That(task2.IsCompleted);
        Assert.That(task3.IsCompleted);
    }

}