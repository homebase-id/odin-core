#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Tests.AppNotifications.Push;

public class PushPreviewSealerTests
{
    private static readonly Guid MessageId = Guid.NewGuid();
    private static readonly Guid ConversationId = Guid.NewGuid();
    private static readonly string DeviceKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    private static FileMetadata EncryptedChatMessage(string content, KeyHeader keyHeader) => new()
    {
        IsEncrypted = true,
        AppData = new AppFileMetaData
        {
            UniqueId = MessageId,
            GroupId = ConversationId,
            FileType = 7878,
            DataType = 0,
            Content = keyHeader.EncryptDataAes(content.ToUtf8ByteArray()).ToBase64(),
        },
    };

    private static int SealedLengthLimit => (PushPreviewSealer.MaxPlaintextBytes + 28 + 2) / 3 * 4;

    [Test]
    public void EncryptedFile_IsOpenedWithTheTransferSecret_AndSealedToTheDevice()
    {
        // The same steps the incoming transfer takes: the key header travels encrypted with the
        // connection's shared secret and is opened with it.
        var keyHeader = KeyHeader.NewRandom16();
        var transferSecret = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        var encryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, ByteArrayUtil.GetRndByteArray(16), ref transferSecret);
        var metadata = EncryptedChatMessage("""{"message":"Hello Frodo 👋"}""", keyHeader);

        var preview = PushPreviewSealer.FromIncomingFile(metadata, () => encryptedKeyHeader.DecryptAesToKeyHeader(ref transferSecret));
        var opened = PushPreviewSealer.Open(PushPreviewSealer.Seal(preview, DeviceKey), DeviceKey);

        Assert.That(opened.Content, Is.EqualTo("""{"message":"Hello Frodo 👋"}"""));
        Assert.That(opened.UniqueId, Is.EqualTo(MessageId));
        Assert.That(opened.GroupId, Is.EqualTo(ConversationId));
        Assert.That(opened.FileType, Is.EqualTo(7878));
        Assert.That(opened.DataType, Is.EqualTo(0));
        Assert.That(opened.ContentTruncated, Is.False);
    }

    [Test]
    public void UnencryptedFile_PassesItsContent_WithoutTouchingTheKeyHeader()
    {
        var metadata = new FileMetadata
        {
            IsEncrypted = false,
            AppData = new AppFileMetaData { UniqueId = MessageId, Content = """{"message":"plain"}""" },
        };

        var preview = PushPreviewSealer.FromIncomingFile(metadata, () => throw new AssertionException("an unencrypted file has no key header to open"));

        Assert.That(preview.Content, Is.EqualTo("""{"message":"plain"}"""));
    }

    [Test]
    public void LongJsonMessage_IsShortenedToFit_AndKeepsItsShape()
    {
        var replyId = Guid.NewGuid().ToString();
        var message = string.Concat(Enumerable.Repeat("Één lange zin met emoji 🧙‍♂️ en tekst. ", 200));
        var content = new JsonObject { ["message"] = message, ["replyId"] = replyId }.ToJsonString();
        var preview = new PushPreview { UniqueId = MessageId, FileType = 7878, Content = content };

        var sealedPreview = PushPreviewSealer.Seal(preview, DeviceKey);
        var opened = PushPreviewSealer.Open(sealedPreview, DeviceKey);

        Assert.That(sealedPreview.Length, Is.LessThanOrEqualTo(SealedLengthLimit));
        Assert.That(opened.ContentTruncated, Is.True);
        var json = JsonNode.Parse(opened.Content!)!.AsObject();
        Assert.That(json["replyId"]!.GetValue<string>(), Is.EqualTo(replyId), "short fields survive");
        var shortened = json["message"]!.GetValue<string>();
        Assert.That(shortened.Length, Is.GreaterThan(500), "the budget is spent on the message, not thrown away");
        Assert.That(message.StartsWith(shortened, StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void Shortening_NeverSplitsASurrogatePair()
    {
        var message = string.Concat(Enumerable.Repeat("😀", 2000));
        var preview = new PushPreview { Content = new JsonObject { ["message"] = message }.ToJsonString() };

        var opened = PushPreviewSealer.Open(PushPreviewSealer.Seal(preview, DeviceKey), DeviceKey);

        var shortened = JsonNode.Parse(opened.Content!)!["message"]!.GetValue<string>();
        Assert.That(shortened.Length % 2, Is.EqualTo(0));
        Assert.That(shortened.EnumerateRunes().All(r => r.Value == 0x1F600), Is.True);
    }

    [Test]
    public void LongNonJsonContent_IsCutDirectly()
    {
        var content = new string('x', 5000);
        var opened = PushPreviewSealer.Open(PushPreviewSealer.Seal(new PushPreview { Content = content }, DeviceKey), DeviceKey);

        Assert.That(opened.ContentTruncated, Is.True);
        Assert.That(opened.Content!.Length, Is.InRange(1000, PushPreviewSealer.MaxPlaintextBytes));
    }

    [Test]
    public void ContentThatCannotBeShortened_IsDropped_SoTheHandlerFetches()
    {
        var bigArray = new JsonArray(Enumerable.Range(0, 2000).Select(i => (JsonNode)JsonValue.Create(i)).ToArray());
        var content = new JsonObject { ["message"] = bigArray }.ToJsonString();
        var opened = PushPreviewSealer.Open(PushPreviewSealer.Seal(new PushPreview { UniqueId = MessageId, Content = content }, DeviceKey), DeviceKey);

        Assert.That(opened.Content, Is.Null);
        Assert.That(opened.ContentTruncated, Is.True);
        Assert.That(opened.UniqueId, Is.EqualTo(MessageId), "the handler still learns which file to fetch");
    }

    [Test]
    public void AnotherKey_CannotOpenThePreview()
    {
        var sealedPreview = PushPreviewSealer.Seal(new PushPreview { Content = "secret" }, DeviceKey);
        var otherKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        Assert.Throws(Is.InstanceOf<CryptographicException>(), () => PushPreviewSealer.Open(sealedPreview, otherKey));
    }

    [TestCase(16, true)]
    [TestCase(15, false)]
    [TestCase(32, false)]
    [TestCase(0, false)]
    public void PreviewKey_MustBeSixteenBytes(int length, bool valid)
    {
        Assert.That(PushPreviewSealer.IsValidKey(Convert.ToBase64String(new byte[length])), Is.EqualTo(valid));
    }

    [Test]
    public void PreviewKey_RejectsNonBase64()
    {
        Assert.That(PushPreviewSealer.IsValidKey("not base64 at all!!"), Is.False);
        Assert.That(PushPreviewSealer.IsValidKey(null), Is.False);
    }

    [Test]
    public void EachDevice_ReceivesOnlyItsOwnPreview()
    {
        var phone = Guid.NewGuid();
        var tablet = Guid.NewGuid();
        var oldPhone = Guid.NewGuid();
        var payload = new PushNotificationPayload
        {
            SenderId = new OdinId("frodo.dotyou.cloud"),
            Options = new AppNotificationOptions { TypeId = ConversationId, TagId = MessageId },
            Timestamp = UnixTimeUtc.Now(),
            EncryptedPreviews = new Dictionary<Guid, string> { [phone] = "sealed-for-phone", [tablet] = "sealed-for-tablet" },
        };

        var phoneData = PushNotificationService.SerializeForDevice(payload, phone);
        var oldPhoneData = PushNotificationService.SerializeForDevice(payload, oldPhone);

        Assert.That(phoneData, Does.Contain("\"encryptedPreview\":\"sealed-for-phone\""));
        Assert.That(phoneData, Does.Not.Contain("sealed-for-tablet"));
        Assert.That(oldPhoneData, Does.Not.Contain("encryptedPreview"), "a device without a preview key sees the payload it always did");
        Assert.That(OdinSystemSerializer.Serialize(payload), Does.Not.Contain("sealed-for"), "web push serializes the payload itself");
    }

    [Test]
    public void APreviewThatWouldOverflowThePush_IsLeftOut()
    {
        var phone = Guid.NewGuid();
        var payload = new PushNotificationPayload
        {
            Options = new AppNotificationOptions { UnEncryptedMessage = new string('m', 2000) },
            EncryptedPreviews = new Dictionary<Guid, string> { [phone] = new('p', 2100) },
        };

        var data = PushNotificationService.SerializeForDevice(payload, phone);

        Assert.That(data, Does.Not.Contain("encryptedPreview"));
        Assert.That(data, Does.Contain(new string('m', 2000)), "the push itself still goes out");
    }

    [Test]
    public void OutboxRecord_KeepsThePreviewsAcrossTheOutbox()
    {
        var phone = Guid.NewGuid();
        var record = new PushNotificationOutboxRecord
        {
            SenderId = new OdinId("frodo.dotyou.cloud"),
            Options = new AppNotificationOptions(),
            EncryptedPreviews = new Dictionary<Guid, string> { [phone] = "sealed" },
        };

        var roundTripped = OdinSystemSerializer.Deserialize<PushNotificationOutboxRecord>(OdinSystemSerializer.Serialize(record))!;

        Assert.That(roundTripped.EncryptedPreviews![phone], Is.EqualTo("sealed"));
    }
}
