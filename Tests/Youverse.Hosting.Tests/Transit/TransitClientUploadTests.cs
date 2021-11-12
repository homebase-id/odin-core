using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
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

        [Test(Description = "Test a transfer where no data is queued")]
        public async Task TestInstantTransfer()
        {
            var sentMessage = GetSmallChatMessage();
            using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise, false))
            {
                //sam to send frodo a data transfer, small enough to send it instantly

                var transitSvc = RestService.For<ITransitTestHttpClient>(client);

                var recipientList = new RecipientList { Recipients = new string[] { _scaffold.Frodo } };
                var response = await transitSvc.SendClientToHost(
                    recipientList,
                    sentMessage.EncryptedKeyHeader,
                    sentMessage.GetMetadataStreamPart(),
                    sentMessage.GetPayloadStreamPart());

                Assert.IsTrue(response.IsSuccessStatusCode);
                var transferResult = response.Content;

                Assert.IsNotNull(transferResult);
                Assert.IsTrue(transferResult.QueuedRecipients.Count == 0);
                Assert.IsTrue(transferResult.SuccessfulRecipients.Count == 1);
                Assert.IsTrue(transferResult.SuccessfulRecipients.First() == _scaffold.Frodo);

                //TODO: determine if we should check outgoing audit to show it was sent
                // var recentAuditResponse = await transitSvc.GetRecentAuditEntries(60, 1, 100);
                // Assert.IsTrue(recentAuditResponse.IsSuccessStatusCode);
            }

            // Now connect as frodo to see if he has a recent transfer from sam matching the file contents
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo, true))
            {
                //TODO: query for the message to see if 

                var expectedMessage = sentMessage;

                //Check audit 
                var transitSvc = RestService.For<ITransitTestHttpClient>(client);
                var recentAuditResponse = await transitSvc.GetRecentAuditEntries(5, 1, 100);

                Assert.IsTrue(recentAuditResponse.IsSuccessStatusCode);
                var entry = recentAuditResponse.Content?.Results.FirstOrDefault(entry => entry.EventId == (int)TransitAuditEvent.Accepted);
                Assert.IsNotNull(entry, "Could not find audit event marked as Accepted");
                
                //I guess I need an api that says give me all transfers from a given DI
                // so in this case I could ge that transfer and compare the file contents?
                //this api is needed for everything - so yea. let's do that
                
            }



            /*
             *so i think in a production scenario we will hve signalr sending a notification for a given app that a transfer has been received
             * but in the case when you're not online.. and sign in.. the signalr notification won't due because it's an 'online thing only'
             * so i thin it makes sense to have an api call which allows the recipient to query all incoming transfers that have not been processed
             */
        }

        private EncryptedKeyHeader EncryptKeyHeader()
        {
            //var publicKey = new byte[] {1, 1, 2, 3, 5, 8, 13, 21};

            var key = new EncryptedKeyHeader()
            {
                Type = EncryptionType.Aes,
                Data = new byte[10]
            };

            return key;
        }

        private TestPayload GetSmallChatMessage()
        {
            var tp = new TestPayload();

            tp.Id = Guid.NewGuid();
            tp.EncryptedKeyHeader = EncryptKeyHeader();
            tp.Metadata = Stream.Null;
            tp.Payload = Stream.Null;

            return tp;
        }
    }
}