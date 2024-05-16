using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Tasks;

namespace Odin.Core.Tests.Tasks;

public class TaskExtensionTests
{
    [Test]
    public void WaitShouldThrowAggregateException()
    {
        Assert.Throws<AggregateException>(() => Fail().Wait());
    }

    [Test]
    public void BlockingWaitShouldThrowRealException()
    {
        Assert.Throws<OdinSystemException>(() => Fail().BlockingWait());
    }

    private static async Task Fail()
    {
        await Task.Delay(1);
        throw new OdinSystemException("OH NO");
    }
}