﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Transit;

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
        [Ignore("wip")]
        public async Task TestInstantTransfer()
        {
            using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise))
            {
                //sam to send frodo a data transfer, small enough to send it instantly

                var transitSvc = RestService.For<ITransitTestHttpClient>(client);

                var recipientList = new RecipientList {Recipients = new string[] {_scaffold.Frodo}};
                var message = GetSmallChatMessage();
                var response = await transitSvc.SendClientToHost(
                    recipientList,
                    message.KeyHeader,
                    message.GetMetadataStreamPart(),
                    message.GetPayloadStreamPart());

                Assert.IsTrue(response.IsSuccessStatusCode);
                var transferResult = response.Content;

                Assert.IsNotNull(transferResult);
                Assert.IsTrue(transferResult.QueuedRecipients.Count == 0);
                Assert.IsTrue(transferResult.SuccessfulRecipients.Count == 1);
                Assert.IsTrue(transferResult.SuccessfulRecipients.First() == _scaffold.Frodo);


                //test that frodo received it. How??
                /*
                 *so i think in a production scenario we will hve signalr sending a notification for a given app that a transfer has been received
                 * but in the case when you're not online.. and sign in.. the signalr notificatino won't due because it's an 'online thing only'
                 * so i thin it makes sense to have an api call which allows the recipient to query all incoming transfers that have not been processed
                 * 
                 */
            }
        }

        private KeyHeader EncryptKeyHeader()
        {
            //var publicKey = new byte[] {1, 1, 2, 3, 5, 8, 13, 21};

            var key = new KeyHeader()
            {
                Id = Guid.NewGuid(),
                EncryptedKey64 = ""
            };

            return key;
        }

        private TestPayload GetSmallChatMessage()
        {
            var tp = new TestPayload();

            tp.Id = Guid.NewGuid();
            tp.KeyHeader = EncryptKeyHeader();
            tp.Metadata = Stream.Null;
            tp.Payload = Stream.Null;

            return tp;
        }
    }
}