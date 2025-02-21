using System.Collections.Generic;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

#nullable enable

public class DictionaryExtensionsTests
{
    [Test]
    public void GetOrDefault_KeyExists_ReturnsValue()
    {
        // Arrange
        var dictionary = new Dictionary<string, int>
        {
            { "key1", 10 },
            { "key2", 20 }
        };

        // Act
        var result = dictionary.GetOrDefault("key1", -1);

        // Assert
        ClassicAssert.AreEqual(10, result);
    }

    [Test]
    public void GetOrDefault_KeyDoesNotExist_ReturnsDefaultValue()
    {
        // Arrange
        var dictionary = new Dictionary<string, int>
        {
            { "key1", 10 },
            { "key2", 20 }
        };

        // Act
        var result = dictionary.GetOrDefault("key3", -1);

        // Assert
        ClassicAssert.AreEqual(-1, result);
    }

    [Test]
    public void GetOrDefault_NullableValueType_KeyExists_ReturnsValue()
    {
        // Arrange
        var dictionary = new Dictionary<string, int?>
        {
            { "key1", 10 },
            { "key2", null }
        };

        // Act
        var result = dictionary.GetOrDefault("key1", -1);

        // Assert
        ClassicAssert.AreEqual(10, result);
    }

    [Test]
    public void GetOrDefault_NullableValueType_KeyExistsWithNullValue_ReturnsNull()
    {
        // Arrange
        var dictionary = new Dictionary<string, int?>
        {
            { "key1", 10 },
            { "key2", null }
        };

        // Act
        var result = dictionary.GetOrDefault("key2", -1);

        // Assert
        ClassicAssert.IsNull(result);
    }

    [Test]
    public void GetOrDefault_NullableValueType_KeyDoesNotExist_ReturnsDefaultValue()
    {
        // Arrange
        var dictionary = new Dictionary<string, int?>
        {
            { "key1", 10 },
            { "key2", null }
        };

        // Act
        var result = dictionary.GetOrDefault("key3", -1);

        // Assert
        ClassicAssert.AreEqual(-1, result);
    }

    [Test]
    public void GetOrDefault_ReferenceType_KeyExists_ReturnsValue()
    {
        // Arrange
        var dictionary = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };

        // Act
        var result = dictionary.GetOrDefault("key1", "default");

        // Assert
        ClassicAssert.AreEqual("value1", result);
    }

    [Test]
    public void GetOrDefault_ReferenceType_KeyDoesNotExist_ReturnsDefaultValue()
    {
        // Arrange
        var dictionary = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };

        // Act
        var result = dictionary.GetOrDefault("key3", "default");

        // Assert
        ClassicAssert.AreEqual("default", result);
    }

    [Test]
    public void GetOrDefault_ReferenceType_KeyExistsWithNullValue_ReturnsNull()
    {
        // Arrange
        var dictionary = new Dictionary<string, string?>
        {
            { "key1", "value1" },
            { "key2", null }
        };

        // Act
        var result = dictionary.GetOrDefault("key2", "default");

        // Assert
        ClassicAssert.IsNull(result);
    }
}
