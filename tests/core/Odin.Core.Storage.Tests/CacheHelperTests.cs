using System;
using System.Diagnostics;
using NUnit.Framework;

namespace Odin.Core.Storage.Tests
{
    public class CacheHelperTests
    {
        [Test]
        public void CacheStringTest()
        {
            var cache = new CacheHelper("test");

            cache.AddOrUpdate("table", "1", "hej");
            cache.AddOrUpdate("table", "2", "");
            cache.AddOrUpdate("table", "3", null);

            var (hit1, o1) = cache.Get("table", "1");
            Debug.Assert(hit1);
            Debug.Assert((string) o1 == "hej");
            var (hit2, o2) = cache.Get("table", "2");
            Debug.Assert(hit2);
            Debug.Assert((string) o2 == "");

            var (hit3, o3) = cache.Get("table", "3");
            Debug.Assert(hit3);
            Debug.Assert((string) o3 == null);
        }

        [Test]
        public void CacheByteTest()
        {
            var cache = new CacheHelper("test");

            var b1 = Guid.NewGuid().ToByteArray();
            var b2 = new byte[] {};

            cache.AddOrUpdate("table", "1", b1);
            cache.AddOrUpdate("table", "2", b2);
            cache.AddOrUpdate("table", "3", null);

            var (hit1, r1) = cache.Get("table", "1");
            Debug.Assert(hit1);
            Debug.Assert(ByteArrayUtil.muidcmp((byte[]) r1, b1) == 0);
            var (hit2, r2) = cache.Get("table", "2");
            Debug.Assert(((byte[])r2).Length == 0);
            Debug.Assert(hit2);
            var (hit3, r3) = cache.Get("table", "3");
            Debug.Assert((byte[])r3 == null);
            Debug.Assert(hit3);
        }

        private class testItem
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public byte[] Value { get; set; }
        }

        [Test]
        public void CacheObjectTest()
        {
            var cache = new CacheHelper("test");

            var b1 = new testItem() { Id = Guid.NewGuid(), Name = "hej", Value = Guid.NewGuid().ToByteArray() };
            var b2 = new testItem { };

            cache.AddOrUpdate("table", "1", b1);
            cache.AddOrUpdate("table", "2", b2);
            cache.AddOrUpdate("table", "3", null);

            var (hit1, r1) = cache.Get("table", "1");
            Debug.Assert(hit1);
            Debug.Assert(ByteArrayUtil.muidcmp(((testItem)r1).Id, b1.Id) == 0);
            Debug.Assert(((testItem)r1).Name == b1.Name);
            Debug.Assert(ByteArrayUtil.muidcmp(((testItem)r1).Value, b1.Value) == 0);

            var (hit2, r2) = cache.Get("table", "2");
            Debug.Assert(hit2);
            Debug.Assert(ByteArrayUtil.muidcmp(((testItem)r2).Id, b2.Id) == 0);
            Debug.Assert(((testItem)r2).Name == b2.Name);
            Debug.Assert(ByteArrayUtil.muidcmp(((testItem)r2).Value, b2.Value) == 0);

            var (hit3, r3) = cache.Get("table", "3");
            Debug.Assert(hit3);
            Debug.Assert((testItem) r3 == null);
        }
    }
}