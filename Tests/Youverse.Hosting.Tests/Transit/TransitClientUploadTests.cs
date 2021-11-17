using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Audit;

namespace Youverse.Hosting.Tests.Transit
{
    public class TransitClientUploadTests
    {
        private TestScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new TestScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test(Description = "Test basic transfer")]
        public async Task TestBasicTransfer()
        {
            var sentMessage = GetSmallChatMessage();
            using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise, false))
            {
                //sam to send frodo a data transfer, small enough to send it instantly

                var transitSvc = RestService.For<ITransitClientToHostHttpClient>(client);

                var recipientList = new RecipientList { Recipients = new[] { _scaffold.Frodo } };
                var response = await transitSvc.SendClientToHost(
                    recipientList,
                    sentMessage.TransferEncryptedKeyHeader,
                    sentMessage.GetMetadataStreamPart(),
                    sentMessage.GetPayloadStreamPart());

                Assert.IsTrue(response.IsSuccessStatusCode);
                var transferResult = response.Content;
                Assert.IsNotNull(transferResult);
                Assert.IsFalse(transferResult.FileId == Guid.Empty, "FileId was not set");
                Assert.IsTrue(transferResult.RecipientStatus.Count == 1, "Too many recipient results returned");
                Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(_scaffold.Frodo), "Could not find matching recipient");

                Assert.IsTrue(transferResult.RecipientStatus[_scaffold.Frodo] == TransferStatus.TransferKeyCreated);

                //TODO: how do i check the outbox queue

                //TODO: How do i check the transfer key was populates?

                //TODO: how do i check Pending Transfer Queue?

                //try to hold out for the background job to process
                System.Threading.Thread.Sleep(2 * 1000);

                //TODO: determine if we should check outgoing audit to show it was sent
                // var recentAuditResponse = await transitSvc.GetRecentAuditEntries(60, 1, 100);
                // Assert.IsTrue(recentAuditResponse.IsSuccessStatusCode);
            }

            // Now connect as frodo to see if he has a recent transfer from sam matching the file contents
            // using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo, true))
            // {
            //     //TODO: query for the message to see if 
            //
            //     var expectedMessage = sentMessage;
            //
            //     //Check audit 
            //     var transitSvc = RestService.For<ITransitTestHttpClient>(client);
            //     var recentAuditResponse = await transitSvc.GetRecentAuditEntries(5, 1, 100);
            //
            //     Assert.IsTrue(recentAuditResponse.IsSuccessStatusCode);
            //     var entry = recentAuditResponse.Content?.Results.FirstOrDefault(entry => entry.EventId == (int) TransitAuditEvent.Accepted);
            //     Assert.IsNotNull(entry, "Could not find audit event marked as Accepted");
            //
            //     //I guess I need an api that says give me all transfers from a given DI
            //     // so in this case I could ge that transfer and compare the file contents?
            //     //this api is needed for everything - so yea. let's do that
            // }


            /*
             *so i think in a production scenario we will hve signalr sending a notification for a given app that a transfer has been received
             * but in the case when you're not online.. and sign in.. the signalr notification won't due because it's an 'online thing only'
             * so i thin it makes sense to have an api call which allows the recipient to query all incoming transfers that have not been processed
             */
        }

        private TestPayload GetSmallChatMessage()
        {
            var tp = new TestPayload();

            tp.Id = Guid.NewGuid();

            var data = Guid.Empty.ToByteArray();
            tp.TransferEncryptedKeyHeader = Convert.ToBase64String(data);
            tp.Metadata = Stream.Null;
            tp.Payload = Stream.Null;
            return tp;
        }
    }
}