using NUnit.Framework;
using System;
using Youverse.Core.Storage.Sqlite.DriveDatabase;

namespace DriveDatabaseTests
{
    public class QueryBatchCursorTests
    {
        [Test]
        public void EmptyStateTest()
        {
            var cursor = new QueryBatchCursor();

            var base64 = cursor.ToState();
            Assert.That(base64, Is.Not.Null);
            var newcursor = new QueryBatchCursor(base64);
            Assert.That(newcursor, Is.Not.Null);
            Assert.That(newcursor.pagingCursor == null);
            Assert.That(newcursor.nextBoundaryCursor== null);
            Assert.That(newcursor.currentBoundaryCursor == null);

            Assert.Pass();
        }

        [Test]
        public void SetPCTest()
        {
            var cursor = new QueryBatchCursor();
            cursor.pagingCursor = Guid.NewGuid().ToByteArray();

            var base64 = cursor.ToState();
            Assert.That(base64, Is.Not.Null);
            var newcursor = new QueryBatchCursor(base64);
            Assert.That(newcursor, Is.Not.Null);
            Assert.That(newcursor.pagingCursor != null);
            Assert.That(newcursor.nextBoundaryCursor == null);
            Assert.That(newcursor.currentBoundaryCursor == null);

            Assert.Pass();
        }

        [Test]
        public void SetNBCTest()
        {
            var cursor = new QueryBatchCursor();
            cursor.nextBoundaryCursor = Guid.NewGuid().ToByteArray();

            var base64 = cursor.ToState();
            Assert.That(base64, Is.Not.Null);
            var newcursor = new QueryBatchCursor(base64);
            Assert.That(newcursor, Is.Not.Null);
            Assert.That(newcursor.pagingCursor == null);
            Assert.That(newcursor.nextBoundaryCursor != null);
            Assert.That(newcursor.currentBoundaryCursor == null);

            Assert.Pass();
        }

        [Test]
        public void SetCBCTest()
        {
            var cursor = new QueryBatchCursor();
            cursor.currentBoundaryCursor = Guid.NewGuid().ToByteArray();

            var base64 = cursor.ToState();
            Assert.That(base64, Is.Not.Null);
            var newcursor = new QueryBatchCursor(base64);
            Assert.That(newcursor, Is.Not.Null);
            Assert.That(newcursor.pagingCursor == null);
            Assert.That(newcursor.nextBoundaryCursor == null);
            Assert.That(newcursor.currentBoundaryCursor != null);

            Assert.Pass();
        }
    }
}