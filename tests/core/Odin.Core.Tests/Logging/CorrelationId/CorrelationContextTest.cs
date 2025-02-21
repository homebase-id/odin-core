using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Logging.CorrelationId;

namespace Odin.Core.Tests.Logging.CorrelationId;

[TestFixture]
public class CorrelationContextTest
{
    [Test]
    public async Task CanCreateNewIdAcrossAsyncContexts1()
    {
        var id1 = "";
        var id2 = "";

        var tasks = new Task[]
        {
            Task.Run(() =>
            {
                var cc1 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                ClassicAssert.False(string.IsNullOrEmpty(cc1.Id));
                id1 = cc1.Id;
            }),
            Task.Run(() =>
            {
                var cc2 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                ClassicAssert.False(string.IsNullOrEmpty(cc2.Id));
                id2 = cc2.Id;

                var thread = new Thread(() =>
                {
                    var cc3 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                    ClassicAssert.False(string.IsNullOrEmpty(cc3.Id));
                    ClassicAssert.AreEqual(id2, cc3.Id);

                    Task.Run(() =>
                    {
                        var cc4 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                        ClassicAssert.False(string.IsNullOrEmpty(cc4.Id));
                        ClassicAssert.AreEqual(id2, cc4.Id);
                    }).Wait();

                    var flow = ExecutionContext.SuppressFlow();
                    Task.Run(() =>
                    {
                        var cc5 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                        ClassicAssert.False(string.IsNullOrEmpty(cc5.Id));
                        ClassicAssert.AreNotEqual(id2, cc5.Id);
                    }).Wait();
                    flow.Undo();

                    Task.Run(() =>
                    {
                        var cc6 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                        ClassicAssert.False(string.IsNullOrEmpty(cc6.Id));
                        ClassicAssert.AreEqual(id2, cc6.Id);
                    }).Wait();

                });
                thread.Start();
                thread.Join();
            })
        };

        await Task.WhenAll(tasks);
        ClassicAssert.AreNotEqual(id1, id2);
    }

    //

    [Test]
    public async Task CanCreateNewIdAcrossAsyncContexts2()
    {
        var id1 = "";
        var id2 = "";

        var cc0 = new CorrelationContext(new CorrelationUniqueIdGenerator())
        {
            Id = "000"
        };

        var tasks = new List<Task>
        {
            CreateNewIdAsync1("111"),
            CreateNewIdAsync2("222")
        };

        await Task.WhenAll(tasks);
        ClassicAssert.AreEqual(cc0.Id, "000");
        ClassicAssert.AreEqual(id1, "111");
        ClassicAssert.AreEqual(id2, "222");

        return;

        async Task CreateNewIdAsync1(string s)
        {
            var cc = new CorrelationContext(new CorrelationUniqueIdGenerator())
            {
                Id = s
            };
            await Task.Delay(100);
            id1 = cc.Id;
        }

        async Task CreateNewIdAsync2(string s)
        {
            var cc = new CorrelationContext(new CorrelationUniqueIdGenerator())
            {
                Id = s
            };
            await Task.Delay(10);
            id2 = cc.Id;
        }
    }
}