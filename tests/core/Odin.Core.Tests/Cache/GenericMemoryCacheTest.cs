using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cache;

namespace Odin.Core.Tests.Cache;

#nullable enable

public class GenericMemoryCacheTest
{
    [Test]
    public void ItShouldInsertAndRetrieveValue()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        cache.Set("foo", "bar", Expiration.Relative(TimeSpan.FromSeconds(10)));
        {
            var hit = cache.TryGet("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value);
        }

        cache.Set("foo", new SampleValue(), Expiration.Relative(TimeSpan.FromSeconds(10)));
        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value!.Name);
        }

        var key = RandomNumberGenerator.GetBytes(16);
        cache.Set(key, "bar", Expiration.Relative(TimeSpan.FromSeconds(10)));
        {
            var hit = cache.TryGet(key, out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value);
        }

        cache.Set(key, new SampleValue(), Expiration.Relative(TimeSpan.FromSeconds(10)));
        {
            var hit = cache.TryGet<SampleValue>(key, out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value!.Name);
        }

        cache.Dispose();
    }

    //

    [Test]
    public void ItShouldGetOrCreateNonNullValue()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        {
            var factoryCallCount = 0;
            var value = cache.GetOrCreate("foo", () =>
            {
                factoryCallCount++;
                return "bar";
            }, Expiration.Relative(TimeSpan.FromSeconds(10)));
            ClassicAssert.AreEqual("bar", value);
            ClassicAssert.AreEqual(1, factoryCallCount);
        }
        {
            var factoryCallCount = 0;
            var value = cache.GetOrCreate("foo", () =>
            {
                factoryCallCount++;
                return "bar";
            }, Expiration.Relative(TimeSpan.FromSeconds(10)));
            ClassicAssert.AreEqual("bar", value);
            ClassicAssert.AreEqual(0, factoryCallCount);
        }
        {
            var hit = cache.TryGet("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value);
        }

        {
            var exception = Assert.Throws<InvalidCastException>(() =>
            {
                cache.GetOrCreate("foo", () => new SampleValue(), Expiration.Relative(TimeSpan.FromSeconds(10)));
            });
            ClassicAssert.AreEqual("The item with key 'foo' cannot be cast to type SampleValue.", exception!.Message);
        }

        cache.Remove("foo");

        {
            var value = cache.GetOrCreate("foo", () => new SampleValue(), Expiration.Relative(TimeSpan.FromSeconds(10)));
            ClassicAssert.AreEqual("bar", value!.Name);
        }
        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value!.Name);
        }

        cache.Dispose();
    }

    //

    [Test]
    public async Task ItShouldGetOrCreateNonNullValueAsync()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        {
            var factoryCallCount = 0;
            var value = await cache.GetOrCreateAsync("foo", async () =>
            {
                await Task.Delay(1);
                factoryCallCount++;
                return "bar";
            }, Expiration.Relative(TimeSpan.FromSeconds(10)));
            ClassicAssert.AreEqual("bar", value);
            ClassicAssert.AreEqual(1, factoryCallCount);
        }
        {
            var factoryCallCount = 0;
            var value = await cache.GetOrCreateAsync("foo", async () =>
            {
                await Task.Delay(1);
                factoryCallCount++;
                return "bar";
            }, Expiration.Relative(TimeSpan.FromSeconds(10)));
            ClassicAssert.AreEqual("bar", value);
            ClassicAssert.AreEqual(0, factoryCallCount);
        }
        {
            var hit = cache.TryGet("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value);
        }

        {
            var exception = Assert.ThrowsAsync<InvalidCastException>(async () =>
            {
                await cache.GetOrCreateAsync("foo", async () =>
                {
                    await Task.Delay(1);
                    return new SampleValue();
                }, Expiration.Relative(TimeSpan.FromSeconds(10)));
            });
            ClassicAssert.AreEqual("The item with key 'foo' cannot be cast to type SampleValue.", exception!.Message);
        }

        cache.Remove("foo");

        {
            var value = await cache.GetOrCreateAsync("foo", async () =>
            {
                await Task.Delay(1);
                return new SampleValue();
            }, Expiration.Relative(TimeSpan.FromSeconds(10)));
            ClassicAssert.AreEqual("bar", value!.Name);
        }
        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value!.Name);
        }

        cache.Dispose();
    }

    //

    [Test]
    public void ItShouldGetOrCreateNullValue()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        {
            var factoryCallCount = 0;
            var value = cache.GetOrCreate<object?>("foo", () =>
            {
                factoryCallCount++;
                return null;
            }, Expiration.Relative(TimeSpan.FromSeconds(10)));
            ClassicAssert.IsNull(value);
            ClassicAssert.AreEqual(1, factoryCallCount);
        }
        {
            var factoryCallCount = 0;
            var value = cache.GetOrCreate<object?>("foo", () =>
            {
                factoryCallCount++;
                return null;
            }, Expiration.Relative(TimeSpan.FromSeconds(10)));
            ClassicAssert.IsNull(value);
            ClassicAssert.AreEqual(0, factoryCallCount);
        }
        {
            var hit = cache.TryGet("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.IsNull(value);
        }

        cache.Dispose();
    }

    //

    [Test]
    public async Task ItShouldGetOrCreateNullValueAsync()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        {
            var factoryCallCount = 0;
            var value = await cache.GetOrCreateAsync<object?>("foo", async () =>
            {
                await Task.Delay(1);
                factoryCallCount++;
                return null;
            }, Expiration.Relative(TimeSpan.FromSeconds(10)));
            ClassicAssert.IsNull(value);
            ClassicAssert.AreEqual(1, factoryCallCount);
        }
        {
            var factoryCallCount = 0;
            var value = await cache.GetOrCreateAsync<object?>("foo", async () =>
            {
                await Task.Delay(1);
                factoryCallCount++;
                return null;
            }, Expiration.Relative(TimeSpan.FromSeconds(10)));
            ClassicAssert.IsNull(value);
            ClassicAssert.AreEqual(0, factoryCallCount);
        }
        {
            var hit = cache.TryGet("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.IsNull(value);
        }

        cache.Dispose();
    }

    //

    [Test]
    public void ItShouldInsertAndRetrieveANullValue()
    {
        var cache = new GenericMemoryCache();

        cache.Set("foo", null, Expiration.Relative(TimeSpan.FromSeconds(10)));
        {
            var hit = cache.TryGet("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.IsNull(value);
        }

        cache.Set("foo", null, Expiration.Relative(TimeSpan.FromSeconds(10)));
        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.IsNull(value);
        }

        cache.Dispose();
    }

    //

    [Test]
    public void ItShouldNotFindAnything()
    {
        var cache = new GenericMemoryCache();

        {
            var hit = cache.TryGet("foo", out var value);
            ClassicAssert.IsFalse(hit);
            ClassicAssert.IsNull(value);
        }

        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            ClassicAssert.IsFalse(hit);
            ClassicAssert.IsNull(value);
        }

        cache.Dispose();
    }

    //

#if !CI_GITHUB
    [Test]
    public void ItShouldEvictEntry()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        cache.Set("foo", new SampleValue(), Expiration.Relative(TimeSpan.FromMilliseconds(10)));
        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value!.Name);
        }

        var key = RandomNumberGenerator.GetBytes(16);
        cache.Set(key, new SampleValue(), Expiration.Relative(TimeSpan.FromMilliseconds(10)));
        {
            var hit = cache.TryGet<SampleValue>(key, out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value!.Name);
        }

        cache.Set("zig", new SampleValue(), Expiration.Absolute(DateTimeOffset.Now + TimeSpan.FromMilliseconds(10)));
        {
            var hit = cache.TryGet<SampleValue>("zig", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value!.Name);
        }

        var key2 = RandomNumberGenerator.GetBytes(16);
        cache.Set(key2, new SampleValue(), Expiration.Absolute(DateTimeOffset.Now + TimeSpan.FromMilliseconds(10)));
        {
            var hit = cache.TryGet<SampleValue>(key2, out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("bar", value!.Name);
        }

        var darth = cache.GetOrCreate("darth", () => "bar", Expiration.Relative(TimeSpan.FromMilliseconds(10)));
        ClassicAssert.AreEqual("bar", darth);

        Thread.Sleep(200);

        {
            var hit = cache.TryGet<SampleValue>("foo", out var value);
            ClassicAssert.IsFalse(hit);
            ClassicAssert.IsNull(value);
        }

        {
            var hit = cache.TryGet<SampleValue>(key, out var value);
            ClassicAssert.IsFalse(hit);
            ClassicAssert.IsNull(value);
        }

        {
            var hit = cache.TryGet<SampleValue>("zig", out var value);
            ClassicAssert.IsFalse(hit);
            ClassicAssert.IsNull(value);
        }

        {
            var hit = cache.TryGet<SampleValue>(key2, out var value);
            ClassicAssert.IsFalse(hit);
            ClassicAssert.IsNull(value);
        }

        {
            var hit = cache.TryGet("darth", out var value);
            ClassicAssert.IsFalse(hit);
            ClassicAssert.IsNull(value);
        }

        cache.Dispose();
    }
#endif

    //

    [Test]
    public void ItShouldThrowOnBadTypeCast()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        {
            cache.Set("foo", new SampleValue(), Expiration.Relative(TimeSpan.FromMilliseconds(100)));

            var exception = Assert.Throws<InvalidCastException>(() =>
            {
                var hit = cache.TryGet<GenericMemoryCacheTest>("foo", out _);
            });
            ClassicAssert.AreEqual("The item with key 'foo' cannot be cast to type GenericMemoryCacheTest.", exception!.Message);
        }

        {
            var key = RandomNumberGenerator.GetBytes(16);
            cache.Set(key, new SampleValue(), Expiration.Relative(TimeSpan.FromMilliseconds(100)));

            var exception = Assert.Throws<InvalidCastException>(() =>
            {
                var hit = cache.TryGet<GenericMemoryCacheTest>(key, out _);
            });
            ClassicAssert.AreEqual($"The item with key '{key.ToBase64()}' cannot be cast to type GenericMemoryCacheTest.", exception!.Message);
        }

        cache.Dispose();
    }

    //

    [Test]
    public void ItShouldLookupConcreteClassBasedOnInterface()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        var key = RandomNumberGenerator.GetBytes(16);
        cache.Set(key, new SomeConcreteClass(), Expiration.Relative(TimeSpan.FromMilliseconds(100)));

        {
            var hit = cache.TryGet<ISomeInterface>(key, out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("Hello", value!.Value);
        }
        // below is exactly the same as above
        {
            var hit = cache.TryGet(key, out ISomeInterface? value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("Hello", value!.Value);
        }

        cache.Dispose();
    }

    //

    [Test]
    public void ItShouldLookupConcreteClassBasedOnParentType()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        var key = RandomNumberGenerator.GetBytes(16);
        cache.Set(key, new SomeConcreteClass(), Expiration.Relative(TimeSpan.FromMilliseconds(100)));

        {
            var hit = cache.TryGet<SomeAbstractClass>(key, out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("Hello", value!.Value);
        }
        // below is exactly the same as above
        {
            var hit = cache.TryGet(key, out SomeAbstractClass? value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("Hello", value!.Value);
        }

        cache.Dispose();
    }

    //

    [Test]
    public void ItShouldRemoveAKey()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        var key = RandomNumberGenerator.GetBytes(16);
        cache.Set(key, "bar", Expiration.Relative(TimeSpan.FromSeconds(10)));
        var hit = cache.TryGet(key, out var value);
        ClassicAssert.IsTrue(hit);
        ClassicAssert.AreEqual("bar", value);

        var exists = cache.Contains(key);
        ClassicAssert.IsTrue(exists);

        value = cache.Remove(key);
        ClassicAssert.AreEqual("bar", value);

        exists = cache.Contains(key);
        ClassicAssert.IsFalse(exists);

        cache.Dispose();
    }

    //

    [Test]
    public void ItShouldClearTheCache()
    {
        // Arrange
        var cache = new GenericMemoryCache();

        var key = RandomNumberGenerator.GetBytes(16);
        cache.Set(key, "bar", Expiration.Relative(TimeSpan.FromSeconds(10)));
        var hit = cache.TryGet(key, out var value);
        ClassicAssert.IsTrue(hit);
        ClassicAssert.AreEqual("bar", value);

        var exists = cache.Contains(key);
        ClassicAssert.IsTrue(exists);

        cache.Clear();

        exists = cache.Contains(key);
        ClassicAssert.IsFalse(exists);

        cache.Dispose();
    }

    //


    //

    [Test]
    public void ItShouldCreateKeyFromStringArray()
    {
        // Arrange
        var cache = new GenericMemoryCache();
        var strings = new [] { "one", "two", "three" };

        var key = cache.GenerateKey("foo", strings);
        ClassicAssert.AreEqual("foo:one:two:three", key);

        cache.Dispose();
    }

    //

    [Test]
    public void ItShouldCreateKeyFromByteArrays()
    {
        // Arrange
        var cache = new GenericMemoryCache();
        var strings = new [] { "foo", "bar", "baz" };

        var byteArrays = new byte[strings.Length][];
        for (var idx = 0; idx < strings.Length; idx++)
        {
            byteArrays[idx] = Encoding.UTF8.GetBytes(strings[idx]);
        }

        var key = cache.GenerateKey("foo", byteArrays);
        ClassicAssert.AreEqual("foo:Zm9v:YmFy:YmF6", key);

        cache.Dispose();
    }

    //

    [Test]
    public void ItShouldSupportDiUsingSpecificGenerics()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddSingleton<
            IGenericMemoryCache<GenericMemoryCacheTest>,
            GenericMemoryCache<GenericMemoryCacheTest>>();

        serviceCollection.AddSingleton<
            IGenericMemoryCache<SampleValue>,
            GenericMemoryCache<SampleValue>>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        {
            var cache = serviceProvider.GetRequiredService<IGenericMemoryCache<GenericMemoryCacheTest>>();
            cache.Set("aaa", "aaa", Expiration.Relative(TimeSpan.FromSeconds(10)));
        }

        {
            var cache = serviceProvider.GetRequiredService<IGenericMemoryCache<SampleValue>>();
            cache.Set("111", "111", Expiration.Relative(TimeSpan.FromSeconds(10)));
        }

        {
            var cache = serviceProvider.GetRequiredService<IGenericMemoryCache<GenericMemoryCacheTest>>();
            var hit = cache.TryGet("aaa", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("aaa", value);

            hit = cache.TryGet("111", out value);
            ClassicAssert.IsFalse(hit);
        }

        {
            var cache = serviceProvider.GetRequiredService<IGenericMemoryCache<SampleValue>>();
            var hit = cache.TryGet("111", out var value);
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("111", value);

            hit = cache.TryGet("aaa", out value);
            ClassicAssert.IsFalse(hit);
        }
    }

    //

    [Test]
    public void ItShouldSupportDiUsingOpenGenericRegistration()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddSingleton(typeof(IGenericMemoryCache<>), typeof(GenericMemoryCache<>));
        serviceCollection.AddTransient<OpenGenericTestA>();
        serviceCollection.AddTransient<OpenGenericTestB>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        {
            var cache = serviceProvider.GetRequiredService<OpenGenericTestA>();
            cache.Store("aaa", "aaa");
        }

        {
            var cache = serviceProvider.GetRequiredService<OpenGenericTestB>();
            cache.Store("111", "111");
        }

        {
            var cache = serviceProvider.GetRequiredService<OpenGenericTestA>();
            var (hit, value) = cache.Load("aaa");
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("aaa", value);

            (hit, _) = cache.Load("111");
            ClassicAssert.IsFalse(hit);
        }

        {
            var cache = serviceProvider.GetRequiredService<OpenGenericTestB>();
            var (hit, value) = cache.Load("111");
            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual("111", value);

            (hit, _) = cache.Load("aaa");
            ClassicAssert.IsFalse(hit);
        }
    }

    private class SampleValue
    {
        public string Name { get; set; } = "bar";
    }

    private interface ISomeInterface
    {
        public string Value { get; }
    }

    private abstract class SomeAbstractClass : ISomeInterface
    {
        public abstract string Value { get; }
    }

    private class SomeConcreteClass : SomeAbstractClass
    {
        public override string Value { get; } = "Hello";
    }

    private class OpenGenericTestA(IGenericMemoryCache<OpenGenericTestA> cache)
    {
        public (bool, string?) Load(string key)
        {
            var hit = cache.TryGet<string>(key, out var value);
            return (hit, value);
        }

        public void Store(string key, string value)
        {
            cache.Set(key, value, Expiration.Relative(TimeSpan.FromSeconds(10)));
        }
    }

    private class OpenGenericTestB(IGenericMemoryCache<OpenGenericTestB> cache)
    {
        public (bool, string?) Load(string key)
        {
            var hit = cache.TryGet<string>(key, out var value);
            return (hit, value);
        }

        public void Store(string key, string value)
        {
            cache.Set(key, value, Expiration.Relative(TimeSpan.FromSeconds(10)));
        }
    }


}