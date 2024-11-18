using System;
using NUnit.Framework;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests
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
            Assert.That(newcursor.stopAtBoundary == null);

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
            Assert.That(newcursor.stopAtBoundary == null);

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
            Assert.That(newcursor.stopAtBoundary == null);

            Assert.Pass();
        }

        [Test]
        public void SetCBCTest()
        {
            var cursor = new QueryBatchCursor();
            cursor.stopAtBoundary = Guid.NewGuid().ToByteArray();

            var base64 = cursor.ToState();
            Assert.That(base64, Is.Not.Null);
            var newcursor = new QueryBatchCursor(base64);
            Assert.That(newcursor, Is.Not.Null);
            Assert.That(newcursor.pagingCursor == null);
            Assert.That(newcursor.nextBoundaryCursor == null);
            Assert.That(newcursor.stopAtBoundary != null);

            Assert.Pass();
        }

        [Test]
        public void BigState()
        {
            var cursor = new QueryBatchCursor();
            cursor.pagingCursor = Guid.NewGuid().ToByteArray();
            cursor.stopAtBoundary = Guid.NewGuid().ToByteArray();
            cursor.nextBoundaryCursor = Guid.NewGuid().ToByteArray();

            var base64 = cursor.ToState();
            Assert.That(base64, Is.Not.Null);
            var bytes = Convert.FromBase64String(base64);
            Assert.AreEqual(bytes.Length, 16+16+16);

            var c264 = new QueryBatchCursor(base64).ToState();

            Assert.AreEqual(c264, base64);
            Assert.Pass();
        }


        [Test]
        public void BigStateWithUserDate()
        {
            var cursor = new QueryBatchCursor();
            cursor.pagingCursor = Guid.NewGuid().ToByteArray();
            cursor.stopAtBoundary = Guid.NewGuid().ToByteArray();
            cursor.nextBoundaryCursor = Guid.NewGuid().ToByteArray();
            cursor.userDateNextBoundaryCursor = UnixTimeUtc.Now();
            cursor.userDateStopAtBoundary = UnixTimeUtc.Now();
            cursor.userDatePagingCursor = UnixTimeUtc.Now();

            var base64 = cursor.ToState();
            Assert.That(base64, Is.Not.Null);
            var bytes = Convert.FromBase64String(base64);
            Assert.AreEqual(bytes.Length, 16 + 16 + 16 + 3 * 1 + 3 * 8);

            var c2base64 = new QueryBatchCursor(base64).ToState();
            Assert.AreEqual(base64, c2base64);

            cursor.userDateNextBoundaryCursor = null;
            cursor.userDatePagingCursor = null;

            base64 = cursor.ToState();
            Assert.That(base64, Is.Not.Null);
            bytes = Convert.FromBase64String(base64);
            c2base64 = new QueryBatchCursor(base64).ToState();
            Assert.AreEqual(base64, c2base64);

            Assert.Pass();
        }
    }
}