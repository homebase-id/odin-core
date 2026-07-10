using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Util;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Membership.YouAuth;

namespace Odin.Services.Tests.Serialization;

/// <summary>
/// Guards the on-disk JSON contract for the ExchangeGrant/AccessRegistration/AccessExchangeGrant
/// renames (KeyStore/ServerHalfOfClientKey/PeerKeyStore). The fixture blobs below are frozen
/// snapshots in the LEGACY property-name format that existing tenant databases contain; the
/// renamed properties must keep reading them via their [JsonPropertyName] shims, and must keep
/// WRITING the legacy names so a version rollback also stays safe. If one of these tests fails,
/// a shim was removed or a nested type's serialized shape changed — either breaks every stored
/// ICR, app registration, client registration, and sent connection request.
///
/// Regenerate the fixtures with the [Explicit] GenerateFixtures test at the bottom (it strips
/// the post-rename PeerKeyStore fields so the blobs stay old-format).
/// </summary>
[TestFixture]
public class LegacyBlobSerializationTests
{
    // Key material the fixture blobs were generated with (see GenerateFixtures)
    private static byte[] Seq(int start) => Enumerable.Range(start, 16).Select(i => (byte)i).ToArray();
    private static SensitiveByteArray MasterKey => Seq(1).ToSensitiveByteArray();
    private static byte[] KeyStoreKey => Seq(17);
    private static byte[] SharedSecret => Seq(33);
    private static byte[] StorageKey => Seq(49);
    private static byte[] IcrKey => Seq(65);
    private const string ClientHalfKey64 = "//31B7vlB+uAQqN9edL0uA==";

    private static readonly Guid CircleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RegistrationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ClientAuthenticationToken ClientAuthToken => new()
    {
        Id = RegistrationId,
        AccessTokenHalfKey = Convert.FromBase64String(ClientHalfKey64).ToSensitiveByteArray()
    };

    // Sent-request blob as stored by _sentRequestValueStorage (CircleNetworkRequestService)
    private const string LegacyConnectionRequestBlob =
        """
        {"senderOdinId":"frodo.dotyou.cloud","receivedTimestampMilliseconds":1700000000000,"outgoingRequestTimestampId":"88888888-8888-8888-8888-888888888888","clientAccessToken64":null,"pendingAccessExchangeGrant":{"masterKeyEncryptedKeyStoreKey":{"keyEncrypted":"Cq45S5Mn61vaYGQa1ArbDzmqEEGvtMzbQVQXWSQxNFA=","keyIV":"yhhWvFxPH7MK1DglklEmMA==","keyHash":"jGEOA/d9lXV5fsgd/tcF6w=="},"circleGrants":{"22222222-2222-2222-2222-222222222222":{"circleId":"22222222222222222222222222222222","permissionSet":{"keys":[10,30]},"keyStoreKeyEncryptedDriveGrants":[{"driveId":"44444444-4444-4444-4444-444444444444","permissionedDrive":{"drive":{"alias":"55555555555555555555555555555555","type":"66666666666666666666666666666666"},"permission":"read","temporalReadWindowSeconds":null},"keyStoreKeyEncryptedStorageKey":{"keyEncrypted":"AkUtbFxtMA1/ltXnW94vZcYgMHUaUMIBB+0jHGnTS4U=","keyIV":"vU/EZFZrIdBjO5EiZ5/lTQ==","keyHash":"w6f0qjK0mB6bbZ5WEOBosA=="}}]}},"appGrants":{"33333333-3333-3333-3333-333333333333":{"22222222-2222-2222-2222-222222222222":{"appId":"33333333333333333333333333333333","circleId":"22222222222222222222222222222222","permissionSet":{"keys":[10]},"keyStoreKeyEncryptedDriveGrants":[{"driveId":"44444444-4444-4444-4444-444444444444","permissionedDrive":{"drive":{"alias":"55555555555555555555555555555555","type":"66666666666666666666666666666666"},"permission":"read","temporalReadWindowSeconds":null},"keyStoreKeyEncryptedStorageKey":{"keyEncrypted":"AkUtbFxtMA1/ltXnW94vZcYgMHUaUMIBB+0jHGnTS4U=","keyIV":"vU/EZFZrIdBjO5EiZ5/lTQ==","keyHash":"w6f0qjK0mB6bbZ5WEOBosA=="}}]}}},"accessRegistration":{"id":"11111111111111111111111111111111","accessRegistrationClientType":"other","created":1700000000000,"clientAccessKeyEncryptedKeyStoreKey":{"keyEncrypted":"M2zJO0ZOCQLyjqo5OmsD/Q==","keyHash":"YbE/eZRKH+WJramIG0wMKw=="},"accessKeyStoreKeyEncryptedSharedSecret":{"keyEncrypted":"qjPaLqK43fkYxpjywor0lnbqzfKBybzuQIKEFer1wlw=","keyIV":"kCBqKUvU5ki4D7SNZ+mxIg==","keyHash":"yP2gppZX8Vs1fBARvi4jDw=="},"isRevoked":false,"accessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey":{"keyEncrypted":"WXM/Jp/nPCQCTbyHKTxR8+EL1LhtXQlHo8Jpjdgk7+A=","keyIV":"xBsCjWSsDWX22gcnuZKoAQ==","keyHash":"nMbIArkvGnZ7qaO7YFU6LA=="}},"isRevoked":false},"tempEncryptedIcrKey":{"keyEncrypted":"4gElgEbYqaqCn2NV8reOrXHylcFvGjjkENwlQMcenkc=","keyIV":"qKGJWRCdNx2rkxpIlvR5fA==","keyHash":"1km5l3RCjtNTxRU84Yv0gQ=="},"tempEncryptedFeedDriveStorageKey":null,"tempRawKey":null,"verificationRandomCode":"99999999-9999-9999-9999-999999999999","verificationHash":null,"id":"8b08a52f-5709-4b8c-86f1-5dc6d57c1beb","contactData":null,"recipient":"sam.dotyou.cloud","message":"hello","circleIds":null,"introducerOdinId":null,"connectionRequestOrigin":"identityOwner"}
        """;

    // Connections-table blob (CircleNetworkStorage.IcrAccessRecord)
    private const string LegacyIcrAccessRecordBlob =
        """
        {"accessGrant":{"masterKeyEncryptedKeyStoreKey":{"keyEncrypted":"Cq45S5Mn61vaYGQa1ArbDzmqEEGvtMzbQVQXWSQxNFA=","keyIV":"yhhWvFxPH7MK1DglklEmMA==","keyHash":"jGEOA/d9lXV5fsgd/tcF6w=="},"circleGrants":{"22222222-2222-2222-2222-222222222222":{"circleId":"22222222222222222222222222222222","permissionSet":{"keys":[10,30]},"keyStoreKeyEncryptedDriveGrants":[{"driveId":"44444444-4444-4444-4444-444444444444","permissionedDrive":{"drive":{"alias":"55555555555555555555555555555555","type":"66666666666666666666666666666666"},"permission":"read","temporalReadWindowSeconds":null},"keyStoreKeyEncryptedStorageKey":{"keyEncrypted":"AkUtbFxtMA1/ltXnW94vZcYgMHUaUMIBB+0jHGnTS4U=","keyIV":"vU/EZFZrIdBjO5EiZ5/lTQ==","keyHash":"w6f0qjK0mB6bbZ5WEOBosA=="}}]}},"appGrants":{"33333333-3333-3333-3333-333333333333":{"22222222-2222-2222-2222-222222222222":{"appId":"33333333333333333333333333333333","circleId":"22222222222222222222222222222222","permissionSet":{"keys":[10]},"keyStoreKeyEncryptedDriveGrants":[{"driveId":"44444444-4444-4444-4444-444444444444","permissionedDrive":{"drive":{"alias":"55555555555555555555555555555555","type":"66666666666666666666666666666666"},"permission":"read","temporalReadWindowSeconds":null},"keyStoreKeyEncryptedStorageKey":{"keyEncrypted":"AkUtbFxtMA1/ltXnW94vZcYgMHUaUMIBB+0jHGnTS4U=","keyIV":"vU/EZFZrIdBjO5EiZ5/lTQ==","keyHash":"w6f0qjK0mB6bbZ5WEOBosA=="}}]}}},"accessRegistration":{"id":"11111111111111111111111111111111","accessRegistrationClientType":"other","created":1700000000000,"clientAccessKeyEncryptedKeyStoreKey":{"keyEncrypted":"M2zJO0ZOCQLyjqo5OmsD/Q==","keyHash":"YbE/eZRKH+WJramIG0wMKw=="},"accessKeyStoreKeyEncryptedSharedSecret":{"keyEncrypted":"qjPaLqK43fkYxpjywor0lnbqzfKBybzuQIKEFer1wlw=","keyIV":"kCBqKUvU5ki4D7SNZ+mxIg==","keyHash":"yP2gppZX8Vs1fBARvi4jDw=="},"isRevoked":false,"accessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey":{"keyEncrypted":"WXM/Jp/nPCQCTbyHKTxR8+EL1LhtXQlHo8Jpjdgk7+A=","keyIV":"xBsCjWSsDWX22gcnuZKoAQ==","keyHash":"nMbIArkvGnZ7qaO7YFU6LA=="}},"isRevoked":false},"encryptedClientAccessToken":{"keyEncrypted":"UvC42Wf0r13JaYqaCTNqz4UO5rbKbRpt2DQNc9P2tXE=","keyIV":"fXoYuTBKEihGiUEx1w7yEg==","keyHash":"GYH1zAcIMrOzFDvtRTgBVQ=="},"weakClientAccessToken":null,"weakKeyStoreKey":null,"originalContactData":null,"introducerOdinId":null,"verificationHash64":null,"connectionOrigin":"identityowner"}
        """;

    // KeyThreeValue blob written by AppRegistrationService
    private const string LegacyAppRegistrationBlob =
        """
        {"appId":"33333333333333333333333333333333","name":"test app","authorizedCircles":["22222222-2222-2222-2222-222222222222"],"circleMemberPermissionGrant":null,"grant":{"created":1700000000000,"modified":1700000000000,"masterKeyEncryptedKeyStoreKey":{"keyEncrypted":"pwv7TvqiP84g0IVG4p6SdiQW5AOXsGwqml8IcRTqxww=","keyIV":"7OROcLcgi1HvUovsIil6ng==","keyHash":"qp0WzxwSAZec+HvUTq9ZRQ=="},"isRevoked":false,"keyStoreKeyEncryptedDriveGrants":[{"driveId":"44444444-4444-4444-4444-444444444444","permissionedDrive":{"drive":{"alias":"55555555555555555555555555555555","type":"66666666666666666666666666666666"},"permission":"read","temporalReadWindowSeconds":null},"keyStoreKeyEncryptedStorageKey":{"keyEncrypted":"AkUtbFxtMA1/ltXnW94vZcYgMHUaUMIBB+0jHGnTS4U=","keyIV":"vU/EZFZrIdBjO5EiZ5/lTQ==","keyHash":"w6f0qjK0mB6bbZ5WEOBosA=="}}],"permissionSet":{"keys":[10,30]},"keyStoreKeyEncryptedIcrKey":{"keyEncrypted":"fI57oOZL2yuv31Ut0gm4UkBoAtg4LbwIZXqJuF2Qr6o=","keyIV":"lE2BENAJ11mp9/iycYlhDw==","keyHash":"6qWx3rTWbpdRoffGBvbs8g=="}},"corsHostName":null}
        """;

    private const string LegacyAppClientRegistrationBlob =
        """
        {"appId":"33333333333333333333333333333333","accessRegistration":{"id":"11111111111111111111111111111111","accessRegistrationClientType":"other","created":1700000000000,"clientAccessKeyEncryptedKeyStoreKey":{"keyEncrypted":"M2zJO0ZOCQLyjqo5OmsD/Q==","keyHash":"YbE/eZRKH+WJramIG0wMKw=="},"accessKeyStoreKeyEncryptedSharedSecret":{"keyEncrypted":"qjPaLqK43fkYxpjywor0lnbqzfKBybzuQIKEFer1wlw=","keyIV":"kCBqKUvU5ki4D7SNZ+mxIg==","keyHash":"yP2gppZX8Vs1fBARvi4jDw=="},"isRevoked":false,"accessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey":{"keyEncrypted":"WXM/Jp/nPCQCTbyHKTxR8+EL1LhtXQlHo8Jpjdgk7+A=","keyIV":"xBsCjWSsDWX22gcnuZKoAQ==","keyHash":"nMbIArkvGnZ7qaO7YFU6LA=="}},"friendlyName":"app client","id":"11111111-1111-1111-1111-111111111111","issuedTo":"sam.dotyou.cloud","type":200,"timeToLiveSeconds":31536000,"categoryId":"33333333-3333-3333-3333-333333333333"}
        """;

    private const string LegacyYouAuthDomainClientBlob =
        """
        {"domain":"photos.odin.earth","accessRegistration":{"id":"11111111111111111111111111111111","accessRegistrationClientType":"other","created":1700000000000,"clientAccessKeyEncryptedKeyStoreKey":{"keyEncrypted":"M2zJO0ZOCQLyjqo5OmsD/Q==","keyHash":"YbE/eZRKH+WJramIG0wMKw=="},"accessKeyStoreKeyEncryptedSharedSecret":{"keyEncrypted":"qjPaLqK43fkYxpjywor0lnbqzfKBybzuQIKEFer1wlw=","keyIV":"kCBqKUvU5ki4D7SNZ+mxIg==","keyHash":"yP2gppZX8Vs1fBARvi4jDw=="},"isRevoked":false,"accessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey":{"keyEncrypted":"WXM/Jp/nPCQCTbyHKTxR8+EL1LhtXQlHo8Jpjdgk7+A=","keyIV":"xBsCjWSsDWX22gcnuZKoAQ==","keyHash":"nMbIArkvGnZ7qaO7YFU6LA=="}},"friendlyName":"domain client","id":"11111111-1111-1111-1111-111111111111","issuedTo":"photos.odin.earth","type":408,"timeToLiveSeconds":0,"categoryId":"83742ae7-e66d-45e6-82a6-6a003c960b39"}
        """;

    private const string LegacyPeerIcrClientBlob =
        """
        {"identity":"frodo.dotyou.cloud","accessRegistration":{"id":"11111111111111111111111111111111","accessRegistrationClientType":"other","created":1700000000000,"clientAccessKeyEncryptedKeyStoreKey":{"keyEncrypted":"M2zJO0ZOCQLyjqo5OmsD/Q==","keyHash":"YbE/eZRKH+WJramIG0wMKw=="},"accessKeyStoreKeyEncryptedSharedSecret":{"keyEncrypted":"qjPaLqK43fkYxpjywor0lnbqzfKBybzuQIKEFer1wlw=","keyIV":"kCBqKUvU5ki4D7SNZ+mxIg==","keyHash":"yP2gppZX8Vs1fBARvi4jDw=="},"isRevoked":false,"accessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey":{"keyEncrypted":"WXM/Jp/nPCQCTbyHKTxR8+EL1LhtXQlHo8Jpjdgk7+A=","keyIV":"xBsCjWSsDWX22gcnuZKoAQ==","keyHash":"nMbIArkvGnZ7qaO7YFU6LA=="}},"id":"11111111-1111-1111-1111-111111111111","issuedTo":"frodo.dotyou.cloud","type":300,"timeToLiveSeconds":630720000,"categoryId":"87ae004a-27db-42e8-850e-35effd8cca1d"}
        """;

    [Test]
    public void ConnectionRequest_LegacySentRequestBlob_Deserializes()
    {
        var request = OdinSystemSerializer.Deserialize<ConnectionRequest>(LegacyConnectionRequestBlob);

        Assert.That(request.PendingPeerKeyStore, Is.Not.Null,
            "pendingAccessExchangeGrant did not map to PendingPeerKeyStore - in-flight sent requests would lose their grant");
        AssertPeerKeyStoreIsIntact(request.PendingPeerKeyStore);
        Assert.That(request.TempEncryptedIcrKey.DecryptKeyClone(KeyStoreKey.ToSensitiveByteArray()).GetKey(), Is.EqualTo(IcrKey));
    }

    [Test]
    public void IcrAccessRecord_LegacyConnectionsBlob_Deserializes()
    {
        var record = OdinSystemSerializer.Deserialize<IcrAccessRecord>(LegacyIcrAccessRecordBlob);

        Assert.That(record.PeerKeyStore, Is.Not.Null, "accessGrant did not map to PeerKeyStore");
        AssertPeerKeyStoreIsIntact(record.PeerKeyStore);
    }

    [Test]
    public void AppRegistration_LegacyBlob_Deserializes()
    {
        var registration = OdinSystemSerializer.Deserialize<AppRegistration>(LegacyAppRegistrationBlob);

        Assert.That(registration.AppKeyStore, Is.Not.Null, "grant did not map to AppKeyStore");
        Assert.That(registration.AppKeyStore.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(MasterKey).GetKey(), Is.EqualTo(KeyStoreKey));
        Assert.That(registration.AppKeyStore.DriveGrants, Has.Count.EqualTo(1),
            "keyStoreKeyEncryptedDriveGrants did not map to DriveGrants");
        Assert.That(registration.AppKeyStore.DriveGrants[0].KeyStoreKeyEncryptedStorageKey
            .DecryptKeyClone(KeyStoreKey.ToSensitiveByteArray()).GetKey(), Is.EqualTo(StorageKey));
        Assert.That(registration.AppKeyStore.KeyStoreKeyEncryptedIcrKey
            .DecryptKeyClone(KeyStoreKey.ToSensitiveByteArray()).GetKey(), Is.EqualTo(IcrKey));
    }

    [Test]
    public void AppClientRegistration_LegacyBlob_Deserializes()
    {
        var client = OdinSystemSerializer.Deserialize<AppClientRegistration>(LegacyAppClientRegistrationBlob)!;
        AssertServerHalfOfClientKeyIsIntact(client.ServerHalfOfClientKey);
    }

    [Test]
    public void YouAuthDomainClient_LegacyBlob_Deserializes()
    {
        var client = OdinSystemSerializer.Deserialize<YouAuthDomainClient>(LegacyYouAuthDomainClientBlob)!;
        AssertServerHalfOfClientKeyIsIntact(client.ServerHalfOfClientKey);
    }

    [Test]
    public void PeerIcrClient_LegacyBlob_Deserializes()
    {
        var client = OdinSystemSerializer.Deserialize<PeerIcrClient>(LegacyPeerIcrClientBlob)!;
        AssertServerHalfOfClientKeyIsIntact(client.ServerHalfOfClientKey);
    }

    /// <summary>
    /// Rollback safety: the renamed properties must keep WRITING the legacy names, so blobs
    /// written by this version stay readable by the previous one.
    /// </summary>
    [Test]
    public void RenamedProperties_SerializeWithLegacyNames()
    {
        var requestJson = OdinSystemSerializer.Serialize(
            OdinSystemSerializer.Deserialize<ConnectionRequest>(LegacyConnectionRequestBlob));
        Assert.That(requestJson, Does.Contain("\"pendingAccessExchangeGrant\""));
        Assert.That(requestJson, Does.Not.Contain("\"pendingPeerKeyStore\""));

        var icrJson = OdinSystemSerializer.Serialize(
            OdinSystemSerializer.Deserialize<IcrAccessRecord>(LegacyIcrAccessRecordBlob));
        Assert.That(icrJson, Does.Contain("\"accessGrant\""));
        Assert.That(icrJson, Does.Contain("\"masterKeyEncryptedKeyStoreKey\""));
        Assert.That(icrJson, Does.Contain("\"accessRegistration\""));
        Assert.That(icrJson, Does.Contain("\"clientAccessKeyEncryptedKeyStoreKey\""));
        Assert.That(icrJson, Does.Contain("\"accessKeyStoreKeyEncryptedSharedSecret\""));
        Assert.That(icrJson, Does.Contain("\"accessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey\""));
        Assert.That(icrJson, Does.Not.Contain("\"peerKeyStore\""));
        Assert.That(icrJson, Does.Not.Contain("\"masterKeyEncryptedPeerKey\""));
        Assert.That(icrJson, Does.Not.Contain("\"peerClientKey\""));
        Assert.That(icrJson, Does.Not.Contain("\"serverHalfOfKey\""));
        Assert.That(icrJson, Does.Not.Contain("\"clientKeyEncryptedSharedSecret\""));
        Assert.That(icrJson, Does.Not.Contain("\"clientKeyEncryptedKeyStoreKey\""));

        var appJson = OdinSystemSerializer.Serialize(
            OdinSystemSerializer.Deserialize<AppRegistration>(LegacyAppRegistrationBlob));
        Assert.That(appJson, Does.Contain("\"grant\""));
        Assert.That(appJson, Does.Contain("\"keyStoreKeyEncryptedDriveGrants\""));
        Assert.That(appJson, Does.Not.Contain("\"appKeyStore\""));
        Assert.That(appJson, Does.Not.Contain("\"driveGrants\""));

        var clientJson = OdinSystemSerializer.Serialize(
            OdinSystemSerializer.Deserialize<AppClientRegistration>(LegacyAppClientRegistrationBlob));
        Assert.That(clientJson, Does.Contain("\"accessRegistration\""));
        Assert.That(clientJson, Does.Not.Contain("\"serverHalfOfClientKey\""));
    }

    private static void AssertPeerKeyStoreIsIntact(PeerKeyStore store)
    {
        Assert.That(store.MasterKeyEncryptedPeerKey, Is.Not.Null,
            "masterKeyEncryptedKeyStoreKey did not map to MasterKeyEncryptedPeerKey");
        Assert.That(store.MasterKeyEncryptedPeerKey.DecryptKeyClone(MasterKey).GetKey(), Is.EqualTo(KeyStoreKey));

        AssertServerHalfOfClientKeyIsIntact(store.PeerClientKey);

        var circleGrant = store.CircleGrants[CircleId];
        var storageKey = circleGrant.KeyStoreKeyEncryptedDriveGrants.Single()
            .KeyStoreKeyEncryptedStorageKey.DecryptKeyClone(KeyStoreKey.ToSensitiveByteArray());
        Assert.That(storageKey.GetKey(), Is.EqualTo(StorageKey));

        Assert.That(store.AppGrants.Values.Single()[CircleId].KeyStoreKeyEncryptedDriveGrants, Has.Count.EqualTo(1));

        // Old blobs predate the write-only deposit fields
        Assert.That(store.WriteOnlyKeyPair, Is.Null);
        Assert.That(store.HasPendingDeposits, Is.False);
    }

    private static void AssertServerHalfOfClientKeyIsIntact(ServerHalfOfClientKey reg)
    {
        Assert.That(reg, Is.Not.Null, "accessRegistration did not map to ServerHalfOfClientKey");
        Assert.That((Guid)reg.Id, Is.EqualTo(RegistrationId));

        // Proves all three shimmed crypto fields deserialized correctly, not just non-null
        var (keyStoreKey, sharedSecret) = reg.DecryptUsingClientAuthenticationToken(ClientAuthToken);
        Assert.That(keyStoreKey.GetKey(), Is.EqualTo(KeyStoreKey));
        Assert.That(sharedSecret.GetKey(), Is.EqualTo(SharedSecret));
    }

    //

    /// <summary>
    /// Regenerates the fixture blobs above. Run it, then paste the printed JSON (and the
    /// clientHalf key) into the constants. Only do this when a nested type's shape changes
    /// legitimately AND a data migration exists for the old blobs.
    /// </summary>
    [Test]
    [Explicit("fixture generator")]
    public void GenerateFixtures()
    {
        var masterKey = Seq(1).ToSensitiveByteArray();
        var keyStoreKey = Seq(17).ToSensitiveByteArray();
        var sharedSecret = Seq(33).ToSensitiveByteArray();
        var storageKey = Seq(49).ToSensitiveByteArray();
        var icrKey = Seq(65).ToSensitiveByteArray();

        var clientKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        var serverHalf = new SymmetricKeyEncryptedXor(clientKey, out var clientHalf);
        Console.WriteLine($"// clientHalf: {Convert.ToBase64String(clientHalf.GetKey())}");

        var serverHalfOfClientKey = new ServerHalfOfClientKey
        {
            Id = new GuidId(RegistrationId),
            AccessRegistrationClientType = AccessRegistrationClientType.Other,
            Created = 1700000000000,
            ServerHalfOfKey = serverHalf,
            ClientKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(clientKey, sharedSecret),
            IsRevoked = false,
            ClientKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(clientKey, keyStoreKey)
        };

        var appId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var driveGrant = new DriveGrant
        {
            DriveId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            PermissionedDrive = new PermissionedDrive
            {
                Drive = new TargetDrive
                {
                    Alias = new GuidId(Guid.Parse("55555555-5555-5555-5555-555555555555")),
                    Type = new GuidId(Guid.Parse("66666666-6666-6666-6666-666666666666"))
                },
                Permission = DrivePermission.Read
            },
            KeyStoreKeyEncryptedStorageKey = new SymmetricKeyEncryptedAes(keyStoreKey, storageKey)
        };

        var peerKeyStore = new PeerKeyStore
        {
            MasterKeyEncryptedPeerKey = new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
            CircleGrants = new Dictionary<Guid, CircleGrant>
            {
                [CircleId] = new()
                {
                    CircleId = new GuidId(CircleId),
                    PermissionSet = new PermissionSet(10, 30),
                    KeyStoreKeyEncryptedDriveGrants = new List<DriveGrant> { driveGrant }
                }
            },
            AppGrants = new Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>
            {
                [appId] = new()
                {
                    [CircleId] = new AppCircleGrant
                    {
                        AppId = new GuidId(appId),
                        CircleId = new GuidId(CircleId),
                        PermissionSet = new PermissionSet(10),
                        KeyStoreKeyEncryptedDriveGrants = new List<DriveGrant> { driveGrant }
                    }
                }
            },
            PeerClientKey = serverHalfOfClientKey,
            IsRevoked = false
        };

        var connectionRequest = new ConnectionRequest
        {
            Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
            Recipient = "sam.dotyou.cloud",
            SenderOdinId = "frodo.dotyou.cloud",
            Message = "hello",
            ReceivedTimestampMilliseconds = 1700000000000,
            OutgoingRequestTimestampId = Guid.Parse("88888888-8888-8888-8888-888888888888"),
            PendingPeerKeyStore = peerKeyStore,
            TempEncryptedIcrKey = new SymmetricKeyEncryptedAes(keyStoreKey, icrKey),
            VerificationRandomCode = Guid.Parse("99999999-9999-9999-9999-999999999999")
        };

        var icrAccessRecord = new IcrAccessRecord
        {
            PeerKeyStore = peerKeyStore,
            EncryptedClientAccessToken = new SymmetricKeyEncryptedAes(icrKey, sharedSecret),
            ConnectionOrigin = "identityowner"
        };

        var appRegistration = new AppRegistration
        {
            AppId = new GuidId(appId),
            Name = "test app",
            AuthorizedCircles = new List<Guid> { CircleId },
            AppKeyStore = new KeyStore
            {
                Created = 1700000000000,
                Modified = 1700000000000,
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
                IsRevoked = false,
                DriveGrants = new List<DriveGrant> { driveGrant },
                PermissionSet = new PermissionSet(10, 30),
                KeyStoreKeyEncryptedIcrKey = new SymmetricKeyEncryptedAes(keyStoreKey, icrKey)
            }
        };

        var appClientRegistration = new AppClientRegistration(
            new OdinId("sam.dotyou.cloud"), new GuidId(appId), "app client", serverHalfOfClientKey);

        var youAuthDomainClient = new YouAuthDomainClient(
            new AsciiDomainName("photos.odin.earth"), "domain client", serverHalfOfClientKey);

        var peerIcrClient = new PeerIcrClient
        {
            Identity = new OdinId("frodo.dotyou.cloud"),
            ServerHalfOfClientKey = serverHalfOfClientKey
        };

        Print("ConnectionRequest", connectionRequest, stripNewPeerKeyStoreFields: true);
        Print("IcrAccessRecord", icrAccessRecord, stripNewPeerKeyStoreFields: true);
        Print("AppRegistration", appRegistration);
        Print("AppClientRegistration", appClientRegistration);
        Print("YouAuthDomainClient", youAuthDomainClient);
        Print("PeerIcrClient", peerIcrClient);
    }

    private static void Print<T>(string name, T value, bool stripNewPeerKeyStoreFields = false)
    {
        var json = OdinSystemSerializer.Serialize(value);
        if (stripNewPeerKeyStoreFields)
        {
            // Old-format blobs predate these PeerKeyStore fields
            var node = JsonNode.Parse(json)!;
            var store = node["pendingAccessExchangeGrant"] ?? node["accessGrant"];
            var obj = store!.AsObject();
            obj.Remove("writeOnlyKeyPair");
            obj.Remove("depositedGrants");
            json = node.ToJsonString();
        }

        Console.WriteLine($"// ---- {name} ----");
        Console.WriteLine(json);
    }
}
