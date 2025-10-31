using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Json;
using Odin.Core.Serialization;

namespace Odin.Core.Tests.Json;

#nullable enable

[TestFixture]
public class JsonEnvelopeTests
{
    private class TestMessage
    {
        public string? Content { get; set; }
        public int Number { get; set; }
    }

    private class AnotherMessage
    {
        public bool Flag { get; set; }
    }

    [Test]
    public void Create_WithValidMessage_CreatesEnvelope()
    {
        // Arrange
        var message = new TestMessage { Content = "test", Number = 42 };

        // Act
        var envelope = JsonEnvelope.Create(message);

        // Assert
        Assert.That(envelope.MessageTypeName, Does.Contain("TestMessage"));
        Assert.That(envelope.MessageJson, Does.Contain("test"));
        Assert.That(envelope.MessageJson, Does.Contain("42"));
    }

    [Test]
    public void GetMessageType_ReturnsCorrectType()
    {
        // Arrange
        var message = new TestMessage { Content = "test", Number = 42 };
        var envelope = JsonEnvelope.Create(message);

        // Act
        var type = envelope.GetMessageType();

        // Assert
        Assert.That(type, Is.EqualTo(typeof(TestMessage)));
    }

    [Test]
    public void GetMessageType_CachesResult()
    {
        // Arrange
        var message = new TestMessage { Content = "test", Number = 42 };
        var envelope = JsonEnvelope.Create(message);

        // Act
        var type1 = envelope.GetMessageType();
        var type2 = envelope.GetMessageType();

        // Assert
        Assert.That(type1, Is.SameAs(type2));
    }

    [Test]
    public void GetMessageType_WithInvalidTypeName_ThrowsException()
    {
        // Arrange
        var envelope = new JsonEnvelope
        {
            MessageTypeName = "NonExistent.Type.Name",
            MessageJson = "{}"
        };

        // Act & Assert
        var ex = Assert.Throws<OdinSystemException>(() => envelope.GetMessageType());
        Assert.That(ex!.Message, Does.Contain("Failed to resolve type"));
        Assert.That(ex.Message, Does.Contain("NonExistent.Type.Name"));
    }

    [Test]
    public void DeserializeMessage_ReturnsOriginalMessage()
    {
        // Arrange
        var original = new TestMessage { Content = "hello", Number = 123 };
        var envelope = JsonEnvelope.Create(original);

        // Act
        var deserialized = envelope.DeserializeMessage();

        // Assert
        Assert.That(deserialized, Is.InstanceOf<TestMessage>());
        var typed = (TestMessage)deserialized!;
        Assert.That(typed.Content, Is.EqualTo("hello"));
        Assert.That(typed.Number, Is.EqualTo(123));
    }

    [Test]
    public void DeserializeMessage_Generic_ReturnsTypedMessage()
    {
        // Arrange
        var original = new TestMessage { Content = "world", Number = 456 };
        var envelope = JsonEnvelope.Create(original);

        // Act
        var deserialized = envelope.DeserializeMessage<TestMessage>()!;

        // Assert
        Assert.That(deserialized.Content, Is.EqualTo("world"));
        Assert.That(deserialized.Number, Is.EqualTo(456));
    }

    [Test]
    public void DeserializeMessage_Generic_WithWrongType_ThrowsException()
    {
        // Arrange
        var original = new TestMessage { Content = "test", Number = 42 };
        var envelope = JsonEnvelope.Create(original);

        // Act & Assert
        var ex = Assert.Throws<OdinSystemException>(() =>
            envelope.DeserializeMessage<AnotherMessage>());
        Assert.That(ex!.Message, Does.Contain("Expected type"));
        Assert.That(ex.Message, Does.Contain("AnotherMessage"));
    }

    [Test]
    public void JsonEnvelope_CanBeSerializedAndDeserialized()
    {
        // Arrange
        var original = new TestMessage { Content = "serialize me", Number = 999 };
        var envelope = JsonEnvelope.Create(original);

        // Act - serialize the envelope itself
        var envelopeJson = OdinSystemSerializer.Serialize(envelope);
        var deserializedEnvelope = OdinSystemSerializer.Deserialize<JsonEnvelope>(envelopeJson);

        // Assert
        Assert.That(deserializedEnvelope, Is.Not.Null);
        Assert.That(deserializedEnvelope!.MessageTypeName, Is.EqualTo(envelope.MessageTypeName));
        Assert.That(deserializedEnvelope.MessageJson, Is.EqualTo(envelope.MessageJson));

        // Verify we can still deserialize the inner message
        var innerMessage = deserializedEnvelope.DeserializeMessage<TestMessage>()!;
        Assert.That(innerMessage.Content, Is.EqualTo("serialize me"));
        Assert.That(innerMessage.Number, Is.EqualTo(999));
    }

    [Test]
    public void Create_WithNullableProperties_HandlesCorrectly()
    {
        // Arrange
        var message = new TestMessage { Content = null, Number = 0 };

        // Act
        var envelope = JsonEnvelope.Create(message);
        var deserialized = envelope.DeserializeMessage<TestMessage>()!;

        // Assert
        Assert.That(deserialized.Content, Is.Null);
        Assert.That(deserialized.Number, Is.EqualTo(0));
    }

    [Test]
    public void Create_WithNullMessage_HandlesCorrectly()
    {
        // Arrange
        TestMessage? message = null;

        // Act
        var envelope = JsonEnvelope.Create(message);
        var deserialized = envelope.DeserializeMessage<TestMessage>()!;

        // Assert
        Assert.That(deserialized, Is.Null);
    }


    [Test]
    public void DeserializeMessage_MultipleCallsWithCachedType_WorksCorrectly()
    {
        // Arrange
        var original = new TestMessage { Content = "cached", Number = 777 };
        var envelope = JsonEnvelope.Create(original);

        // Act - call multiple times to ensure cache doesn't break deserialization
        var result1 = envelope.DeserializeMessage<TestMessage>()!;
        var result2 = envelope.DeserializeMessage<TestMessage>()!;
        var result3 = envelope.DeserializeMessage()!;

        // Assert
        Assert.That(result1.Content, Is.EqualTo("cached"));
        Assert.That(result2.Number, Is.EqualTo(777));
        Assert.That(result3, Is.InstanceOf<TestMessage>());
    }
}