using System;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests
{
    public class QueryBatchCursorTests
    {
        [Test]
        public void TimeCursorEmptyStateTest()
        {
            var cursor = TimeRowCursor.FromJsonOrOldString(null);
            Assert.That(cursor == null);
            cursor = TimeRowCursor.FromJsonOrOldString("");
            Assert.That(cursor == null);
            Assert.Pass();
        }

        [Test]
        public void TimeCursorInvalidStateTest()
        {
            var cursor = TimeRowCursor.FromJsonOrOldString("asdasda");
            Assert.That(cursor == null);
            cursor = TimeRowCursor.FromJsonOrOldString("1,2,3");
            Assert.That(cursor == null);
            Assert.Pass();
        }


        [Test]
        public void TimeStringTests()
        {
            var cursor = TimeRowCursor.FromJsonOrOldString("42");
            Assert.That(cursor.Time == 42);
            Assert.That(cursor.rowId == null);

            cursor = TimeRowCursor.FromJsonOrOldString("42,7");
            Assert.That(cursor.Time == 42);
            Assert.That(cursor.rowId == 7);

            Assert.Pass();
        }


        [Test]
        public void TimeJsonTests()
        {
            var cursor = new TimeRowCursor(42,7);
            var json = cursor.ToJson();

            var copy = TimeRowCursor.FromJson(json);

            Assert.That(cursor.Time == copy.Time);
            Assert.That(cursor.rowId == copy.rowId);

            cursor = new TimeRowCursor(42, null);
            json = cursor.ToJson();

            copy = TimeRowCursor.FromJson(json);

            Assert.That(cursor.Time == copy.Time);
            Assert.That(cursor.rowId == copy.rowId);

            Assert.Pass();
        }


        [Test]
        public void EmptyStateTest()
        {
            var cursor = new QueryBatchCursor();

            var base64 = cursor.ToJson();
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
            cursor.pagingCursor = new TimeRowCursor(new UnixTimeUtc(42), 69);

            var json = cursor.ToJson();
            Assert.That(json, Is.Not.Null);
            var newcursor = new QueryBatchCursor(json);
            Assert.That(newcursor, Is.Not.Null);
            Assert.That(newcursor.pagingCursor != null);
            Assert.That(newcursor.pagingCursor.Time.Equals(cursor.pagingCursor.Time));
            Assert.That(newcursor.pagingCursor.rowId.Equals(cursor.pagingCursor.rowId));
            Assert.That(newcursor.nextBoundaryCursor == null);
            Assert.That(newcursor.stopAtBoundary == null);

            Assert.Pass();
        }

        [Test]
        public void SetNBCTest()
        {
            var cursor = new QueryBatchCursor();
            cursor.nextBoundaryCursor = new TimeRowCursor(new UnixTimeUtc(42), 69); ;

            var base64 = cursor.ToJson();
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
            cursor.stopAtBoundary = new TimeRowCursor(new UnixTimeUtc(42), 69);

            var json = cursor.ToJson();
            Assert.That(json, Is.Not.Null);
            var newcursor = new QueryBatchCursor(json);
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
            cursor.pagingCursor = new TimeRowCursor(new UnixTimeUtc(42), 69); ;
            cursor.stopAtBoundary = new TimeRowCursor(new UnixTimeUtc(43), 70); ;
            cursor.nextBoundaryCursor = new TimeRowCursor(new UnixTimeUtc(44), 71);

            var json = cursor.ToJson();
            Assert.That(json, Is.Not.Null);

            var c264 = new QueryBatchCursor(json).ToJson();

            ClassicAssert.AreEqual(c264, json);
            Assert.Pass();
        }


        [Test]
        public void BigStateWithUserDate()
        {
            var cursor = new QueryBatchCursor();
            cursor.pagingCursor = new TimeRowCursor(new UnixTimeUtc(42), 69);
            cursor.stopAtBoundary = new TimeRowCursor(new UnixTimeUtc(43), 70);
            cursor.nextBoundaryCursor = new TimeRowCursor(new UnixTimeUtc(44), 71);

            var json = cursor.ToJson();
            Assert.That(json, Is.Not.Null);

            var c2base64 = new QueryBatchCursor(json).ToJson();
            ClassicAssert.AreEqual(json, c2base64);

            Assert.Pass();
        }
    }
}