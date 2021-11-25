using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Logging.CorrelationId;

namespace Youverse.Core.Tests.Logging.CorrelationId
{
    [TestFixture]
    public class CorrelationContextTest
    {
        [Test(Description = "Can create new id across async contexts")]
        public async Task CanCreateNewIdAcrossAsyncContexts()
        {
            var id1 = "";
            var id2 = "";

            var tasks = new Task[]
            {
                Task.Run(() =>
                {
                    var cc1 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                    Assert.False(string.IsNullOrEmpty(cc1.Id));
                    id1 = cc1.Id;
                }),
                Task.Run(() =>
                {
                    var cc2 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                    Assert.False(string.IsNullOrEmpty(cc2.Id));
                    id2 = cc2.Id;
                    
                    var thread = new Thread(() => 
                    {
                        var cc3 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                        Assert.False(string.IsNullOrEmpty(cc3.Id));
                        Assert.AreEqual(id2, cc3.Id);
                    
                        Task.Run(() =>
                        {
                            var cc4 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                            Assert.False(string.IsNullOrEmpty(cc4.Id));
                            Assert.AreEqual(id2, cc4.Id);
                        }).Wait();
                        
                        var flow = ExecutionContext.SuppressFlow();
                        Task.Run(() =>
                        {
                            var cc5 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                            Assert.False(string.IsNullOrEmpty(cc5.Id));
                            Assert.AreNotEqual(id2, cc5.Id);
                        }).Wait();
                        flow.Undo();

                        Task.Run(() =>
                        {
                            var cc6 = new CorrelationContext(new CorrelationUniqueIdGenerator());
                            Assert.False(string.IsNullOrEmpty(cc6.Id));
                            Assert.AreEqual(id2, cc6.Id);
                        }).Wait();

                    });
                    thread.Start();
                    thread.Join();
                })
            };

            await Task.WhenAll(tasks);
            Assert.AreNotEqual(id1, id2);
        }

    }
}