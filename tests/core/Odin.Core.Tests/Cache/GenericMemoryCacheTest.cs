using System;
using System.Threading;
using NUnit.Framework;
using Odin.Core.Cache;

namespace Odin.Core.Tests.Cache;

public class GenericMemoryCacheTest
{
    [Test]
    public void ItShouldInsertAndRetrieveValue()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        cache.Set("foo", "bar", TimeSpan.FromSeconds(10));
        {
            var hit = cache.TryGet("foo", out var value);
            Assert.IsTrue(hit);
            Assert.AreEqual("bar", value);
        }

        cache.Set("foo", new SampleValue(), TimeSpan.FromSeconds(10));
        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            Assert.IsTrue(hit);
            Assert.AreEqual("bar", value!.Name);
        }
    }

    //

    [Test]
    public void ItShouldEvictEntry()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        cache.Set("foo", new SampleValue(), TimeSpan.FromMilliseconds(10));
        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            Assert.IsTrue(hit);
            Assert.AreEqual("bar", value!.Name);
        }

        Thread.Sleep(200);

        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            Assert.IsFalse(hit);
            Assert.IsNull(value);
        }
    }

    //

    [Test]
    public void ItShouldThrowOnBadTypeCast()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        cache.Set("foo", new SampleValue(), TimeSpan.FromMilliseconds(100));

        var exception = Assert.Throws<InvalidCastException>(() =>
        {
            var hit = cache.TryGet<GenericMemoryCacheTest>("foo", out _);
        });
        Assert.AreEqual("The item with key 'foo' cannot be cast to type GenericMemoryCacheTest.", exception.Message);
    }

    //

    private class SampleValue
    {
        public string Name { get; set; } = "bar";
    }
}