using System;
using System.Security.Cryptography;
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

        var key = RandomNumberGenerator.GetBytes(16);
        cache.Set(key, "bar", TimeSpan.FromSeconds(10));
        {
            var hit = cache.TryGet(key, out var value);
            Assert.IsTrue(hit);
            Assert.AreEqual("bar", value);
        }

        cache.Set(key, new SampleValue(), TimeSpan.FromSeconds(10));
        {
            var hit = cache.TryGet<SampleValue>(key, out var value);
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

        var key = RandomNumberGenerator.GetBytes(16);
        cache.Set(key, new SampleValue(), TimeSpan.FromMilliseconds(10));
        {
            var hit = cache.TryGet<SampleValue>(key, out var value);
            Assert.IsTrue(hit);
            Assert.AreEqual("bar", value!.Name);
        }

        Thread.Sleep(200);

        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            Assert.IsFalse(hit);
            Assert.IsNull(value);
        }

        {
            var hit = cache.TryGet<SampleValue>(key, out var value);
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

        {
            cache.Set("foo", new SampleValue(), TimeSpan.FromMilliseconds(100));

            var exception = Assert.Throws<InvalidCastException>(() =>
            {
                var hit = cache.TryGet<GenericMemoryCacheTest>("foo", out _);
            });
            Assert.AreEqual("The item with key 'foo' cannot be cast to type GenericMemoryCacheTest.", exception!.Message);
        }

        {
            var key = RandomNumberGenerator.GetBytes(16);
            cache.Set(key, new SampleValue(), TimeSpan.FromMilliseconds(100));

            var exception = Assert.Throws<InvalidCastException>(() =>
            {
                var hit = cache.TryGet<GenericMemoryCacheTest>(key, out _);
            });
            Assert.AreEqual($"The item with key '{key.ToBase64()}' cannot be cast to type GenericMemoryCacheTest.", exception!.Message);
        }

    }

    //

    //

    [Test]
    public void ItShouldRemoveAnKey()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        var key = RandomNumberGenerator.GetBytes(16);
        cache.Set(key, "bar", TimeSpan.FromSeconds(10));
        var hit = cache.TryGet(key, out var value);
        Assert.IsTrue(hit);
        Assert.AreEqual("bar", value);

        var exists = cache.Contains(key);
        Assert.IsTrue(exists);

        value = cache.Remove(key);
        Assert.AreEqual("bar", value);

        exists = cache.Contains(key);
        Assert.IsFalse(exists);
    }


    private class SampleValue
    {
        public string Name { get; set; } = "bar";
    }
}