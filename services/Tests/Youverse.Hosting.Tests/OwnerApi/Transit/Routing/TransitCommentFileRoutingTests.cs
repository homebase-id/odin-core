using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Youverse.Hosting.Tests.OwnerApi.Transit.Routing
{
    public class TransitCommentFileRoutingTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }


        [Test]
        public void CanTransfer_Unencrypted_Comment_S2110()
        {
            Assert.Inconclusive("work in progress");

            /*
             Success Test - Comment
                Valid ReferencedFile (global transit id)
                Sender has storage Key
                Sender has write access
                Upload standard file - encrypted = false
                Upload comment file - encrypted = false
                Should succeed (S2110)
                    Direct write comment
                    Comment is not distributed
                    ReferencedFile summary updated
                    ReferencedFile is distributed to followers
             */
        }

        [Test]
        public void CanTransfer_Encrypted_Comment_S2110()
        {
            Assert.Inconclusive("work in progress");

            /*
             Success Test - Comment
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender has write access
                Sender has storage Key
                Valid ReferencedFile (global transit id)
                Should succeed (S2110)
                    Direct write comment
                    Comment is not distributed
                    ReferencedFile summary updated
                    ReferencedFile is distributed to followers
             */
        }

        [Test]
        public void FailsWhenSenderCannotWriteCommentOnRecipientServer()
        {
            Assert.Inconclusive("work in progress");

            /*
             Failure Test - Comment
                Fails when sender cannot write to target drive on recipients server
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender does not have write access (S2000)
                Sender has storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                throws 403 - S2010
             */
        }

        [Test]
        public void FailsWhenSenderSpecifiesInvalidReferencedFile_S2030()
        {
            Assert.Inconclusive("work in progress");

            /*
             Fails when sender provides invalid  global transit id
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender does not have write access (S2000)
                Sender has storage Key
                Invalid ReferencedFile (global transit id)
                Should fail
                throws Bad Request - S2030
             */
        }

        [Test]
        public void FailsWhenEncryptionDoesNotMatchCommentAndReferencedFile_S2100_Test1()
        {
            Assert.Inconclusive("work in progress");

            /*
             Fails when encryption do not match between from a comment to its ReferencedFile
                Test 1
                Upload standard file - encrypted = true
                Upload comment file - encrypted = false
                Sender does not have write access (S2000)
                Sender has storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                Bad Request (S2100)
             */
        }

        [Test]
        public void FailsWhenEncryptionDoesNotMatchCommentAndReferencedFile_S2100_Test2()
        {
            Assert.Inconclusive("work in progress");

            /*
              Fails when encryption do not match between from a comment to its ReferencedFile

                Test 2
                Upload standard file - encrypted = false
                Upload comment file - encrypted = true
                Sender does not have write access (S2000)
                Sender has storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                Bad Request (S2100)
             */
        }

        [Test]
        public void FailsWhenCommentFileIsEncryptedAndSenderHasNoDriveStorageKeyOnRecipientServer_S2210()
        {
            Assert.Inconclusive("work in progress");

            /*
             Fails when file is encrypted and there is no drive storage key
                Comment:
                Test 1
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender has write access
                Sender does not have storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                403 (question outstanding, why not go to inbox
             */
        }
    }
}