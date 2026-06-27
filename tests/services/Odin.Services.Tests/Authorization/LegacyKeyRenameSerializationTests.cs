
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using Odin.Core;                                   // GuidId, SensitiveByteArray
using Odin.Core.Cryptography.Data;                 // SymmetricKeyEncryptedAes, SymmetricKeyEncryptedXor
using Odin.Core.Serialization;                     // OdinSystemSerializer
using Odin.Core.Time;                              // UnixTimeUtc
using Odin.Services.Authorization.Apps;            // AppRegistration, AppCircleGrant
using Odin.Services.Authorization.ExchangeGrants;  // ExchangeGrant, DriveGrant, AccessRegistration, PermissionedDrive, AccessRegistrationClientType
using Odin.Services.Authorization.Permissions;     // PermissionSet
using Odin.Services.Drives;                        // TargetDrive, DrivePermission
using Odin.Services.Membership.Connections;        // AccessExchangeGrant, CircleGrant

namespace Odin.Services.Tests.Authorization
{
    // ---------------------------------------------------------------------
    // Fixed key material shared by the generator and every assertion.
    // ---------------------------------------------------------------------
    internal static class SnapshotKeys
    {
        public static SensitiveByteArray Key(byte b) => new(Enumerable.Repeat(b, 16).ToArray());

        public static SensitiveByteArray MasterKey         => Key(0x11);
        public static SensitiveByteArray AppKey            => Key(0x22); // ExchangeGrant hub key
        public static SensitiveByteArray PeerKey           => Key(0x33); // AccessExchangeGrant hub key
        public static SensitiveByteArray StorageKey        => Key(0x44);
        public static SensitiveByteArray AccessKeyStoreKey => Key(0x66);

        public static T Load<T>(string json) => OdinSystemSerializer.Deserialize<T>(json);

        public static void AssertKey(SensitiveByteArray actual, SensitiveByteArray expected) =>
            Assert.That(actual.GetKey(), Is.EqualTo(expected.GetKey()));
    }

    // ---------------------------------------------------------------------
    // Builds fully-populated objects. The generator serializes these to make
    // the snapshots; tier-2 serializes them fresh to compare wire names.
    // ---------------------------------------------------------------------
    internal static class SnapshotFactory
    {
        private static SymmetricKeyEncryptedAes Aes(SensitiveByteArray s, SensitiveByteArray d) => new(s, d);
        private static readonly UnixTimeUtc Ts = new(1_700_000_000_000);

        public static DriveGrant DriveGrant() => new()
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = new TargetDrive
                {
                    Alias = Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
                    Type  = Guid.Parse("00000000-0000-0000-0000-0000000000a2")
                },
                Permission = DrivePermission.Read
            },
            KeyStoreKeyEncryptedStorageKey = Aes(SnapshotKeys.AppKey, SnapshotKeys.StorageKey)
        };

        public static ExchangeGrant ExchangeGrant() => new()
        {
            Created = Ts,
            IsRevoked = false,
            MasterKeyEncryptedKeyStoreKey = Aes(SnapshotKeys.MasterKey, SnapshotKeys.AppKey),
            KeyStoreKeyEncryptedDriveGrants = new List<DriveGrant> { DriveGrant() },
            KeyStoreKeyEncryptedIcrKey = Aes(SnapshotKeys.AppKey, SnapshotKeys.Key(0x55)),
            PermissionSet = new PermissionSet()
        };

        public static AccessRegistration AccessRegistration() => new()
        {
            Id = Guid.Parse("00000000-0000-0000-0000-0000000000b1"),
            AccessRegistrationClientType = AccessRegistrationClientType.Other,
            Created = Ts,
            ClientAccessKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedXor(SnapshotKeys.AccessKeyStoreKey, out _),
            AccessKeyStoreKeyEncryptedSharedSecret = Aes(SnapshotKeys.AccessKeyStoreKey, SnapshotKeys.Key(0x77)),
            AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey = Aes(SnapshotKeys.AccessKeyStoreKey, SnapshotKeys.AppKey),
            IsRevoked = false
        };

        public static AccessExchangeGrant AccessExchangeGrant() => new()
        {
            MasterKeyEncryptedKeyStoreKey = Aes(SnapshotKeys.MasterKey, SnapshotKeys.PeerKey),
            IsRevoked = false,
            AccessRegistration = AccessRegistration(),
            CircleGrants = new Dictionary<Guid, CircleGrant>(),
            AppGrants = new Dictionary<Guid, Dictionary<Guid, AppCircleGrant>>()
        };

        public static AppRegistration AppRegistration() => new()
        {
            AppId = Guid.Parse("00000000-0000-0000-0000-0000000000c1"),
            Name = "golden",
            CorsHostName = "example.com",
            AuthorizedCircles = new List<Guid>(),
            Grant = ExchangeGrant()
        };
    }

    // ---------------------------------------------------------------------
    // FROZEN legacy wire format. Generated ONCE before the rename, pasted here.
    // Do NOT regenerate after renaming — these are the immutable old-format truth.
    // ---------------------------------------------------------------------
    internal static class LegacySnapshots
    {
        // Frozen output of PrintLegacySnapshots, captured pre-rename. Do not regenerate after the rename.
        public const string DriveGrant = """{"driveId":"00000000-0000-0000-0000-000000000000","permissionedDrive":{"drive":{"alias":"000000000000000000000000000000a1","type":"000000000000000000000000000000a2"},"permission":"read","temporalReadWindowSeconds":null},"keyStoreKeyEncryptedStorageKey":{"keyEncrypted":"6IkzCfx7eG507mAghvBBBVMlYEwbrzWvoF9LkREvCHs=","keyIV":"F5qcau9AQ5PM4JjdxbQk0A==","keyHash":"4jfHuhuxGAYwFf5EYul8fQ=="}}""";

        public const string ExchangeGrant = """{"created":1700000000000,"modified":0,"masterKeyEncryptedKeyStoreKey":{"keyEncrypted":"rymo9ivn2wmadE2wL+NaT0NhHbLZIUmphF8Bh4QtTsc=","keyIV":"JLO7GMgNoSs0EVD14QH8hg==","keyHash":"T0nRXW1wQxlJ7vqQfx3dSA=="},"isRevoked":false,"keyStoreKeyEncryptedDriveGrants":[{"driveId":"00000000-0000-0000-0000-000000000000","permissionedDrive":{"drive":{"alias":"000000000000000000000000000000a1","type":"000000000000000000000000000000a2"},"permission":"read","temporalReadWindowSeconds":null},"keyStoreKeyEncryptedStorageKey":{"keyEncrypted":"ptghZW84zLA7VnhhAmDhr36/VOkFLGPvtYmxgmN9OQs=","keyIV":"ypkZRwzqpp5U0T7tt3/lDw==","keyHash":"PzRCl/gb/QuoJFh0ECK9og=="}}],"permissionSet":{"keys":[]},"keyStoreKeyEncryptedIcrKey":{"keyEncrypted":"5JcPMfR01EquElkiRvgtxubnSZ5NRX17Sa9/GGtLD6g=","keyIV":"SdL63GegUrhGT0siZqBrxA==","keyHash":"vH+hDJNRCS26ui27wf0zaQ=="}}""";

        public const string AccessRegistration = """{"id":"000000000000000000000000000000b1","accessRegistrationClientType":"other","created":1700000000000,"clientAccessKeyEncryptedKeyStoreKey":{"keyEncrypted":"XmKTvb71xPeNvmjP2WnB1A==","keyHash":"i/BpqOd4CozSmpIlu4fxUA=="},"accessKeyStoreKeyEncryptedSharedSecret":{"keyEncrypted":"Ed8aq2JHXCnh2Hkyu/dGSD5R3o6nopUpx7ZDaf0dI2M=","keyIV":"sdOsLPIpf41TP48SVe4ZOg==","keyHash":"Y+FAuK569rHxwvVj1A25Jg=="},"isRevoked":false,"accessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey":{"keyEncrypted":"wOVqPMBqs3GB1r/ik/I8SAzOt9Gms3LUQd4xRuN40nU=","keyIV":"yxjJrPGgQpv4o0QXyeFJhQ==","keyHash":"GSolOK3zy6daXj5mSALpmQ=="}}""";

        public const string AccessExchangeGrant = """{"masterKeyEncryptedKeyStoreKey":{"keyEncrypted":"01umFf8rHaHugtG7S3Uba1CB26vgst96ICnOE8MOQy8=","keyIV":"M7J6s8iUZlQJP0hyF8DS/Q==","keyHash":"WEgQ9m3phGZ0wOIXidzzMw=="},"circleGrants":{},"appGrants":{},"accessRegistration":{"id":"000000000000000000000000000000b1","accessRegistrationClientType":"other","created":1700000000000,"clientAccessKeyEncryptedKeyStoreKey":{"keyEncrypted":"aEMgTsgggbCNSnq/rrhQGQ==","keyHash":"2qVxKrSYGPUxnjzBcDV05w=="},"accessKeyStoreKeyEncryptedSharedSecret":{"keyEncrypted":"4bIorrBiYFiifIaQ9mYKZI//lk1Dbo/O/2k3Q+e+Cbs=","keyIV":"V/WeYDYJMfhoayRMvZUYiA==","keyHash":"hcdy9GpauMTKll49PHa4lA=="},"isRevoked":false,"accessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey":{"keyEncrypted":"SfEjYcbzXU0kZM635IGjOPNRrU6+UbtaayJCyYoZbxY=","keyIV":"VJdwESyLmpHTojGggKVbuQ==","keyHash":"hqWchXDYE61xX0vRAUb7pQ=="}},"isRevoked":false}""";

        public const string AppRegistration = """{"appId":"000000000000000000000000000000c1","name":"golden","authorizedCircles":[],"circleMemberPermissionGrant":null,"grant":{"created":1700000000000,"modified":0,"masterKeyEncryptedKeyStoreKey":{"keyEncrypted":"4qD4NYUwKMfFNmeUy/Ta+IOcKPk0NfTFZsnL9fh6rfc=","keyIV":"NXixXeT43L8pNBgkrCbyQw==","keyHash":"XoLbGEGFPo1Uy7JBMjrTjQ=="},"isRevoked":false,"keyStoreKeyEncryptedDriveGrants":[{"driveId":"00000000-0000-0000-0000-000000000000","permissionedDrive":{"drive":{"alias":"000000000000000000000000000000a1","type":"000000000000000000000000000000a2"},"permission":"read","temporalReadWindowSeconds":null},"keyStoreKeyEncryptedStorageKey":{"keyEncrypted":"vadkkHVQ8GKnbMLjc+1rOtg3PWnxH/EYQArzj34xXTw=","keyIV":"zGA/rhhEIDeOX3wY0G2Jvw==","keyHash":"Oc1kfuy1e6JyqhqBdzDREg=="}}],"permissionSet":{"keys":[]},"keyStoreKeyEncryptedIcrKey":{"keyEncrypted":"flz2yotoXRDfb7YCV5c6ScqAeKKA/+8lLlH0ioaHv98=","keyIV":"WfjG1nQxsqngE9K3jO1dhw==","keyHash":"rFWdBoDA6Twc5rQuK7AFKg=="}},"corsHostName":"example.com"}""";
    }

    // ---------------------------------------------------------------------
    // Run ONCE before the rename to emit the legacy JSON; paste into LegacySnapshots.
    // ---------------------------------------------------------------------
    [TestFixture]
    public class LegacySnapshotGenerator
    {
        [Explicit("Run once BEFORE the rename, while old property names are live; paste output into LegacySnapshots.")]
        [Test]
        public void PrintLegacySnapshots()
        {
            void Dump(string n, object o) =>
                TestContext.WriteLine($"// ---- {n} ----\n{OdinSystemSerializer.Serialize(o)}\n");

            Dump("DriveGrant",          SnapshotFactory.DriveGrant());
            Dump("ExchangeGrant",       SnapshotFactory.ExchangeGrant());
            Dump("AccessRegistration",  SnapshotFactory.AccessRegistration());
            Dump("AccessExchangeGrant", SnapshotFactory.AccessExchangeGrant());
            Dump("AppRegistration",     SnapshotFactory.AppRegistration());
        }
    }

    // ---------------------------------------------------------------------
    // TIER 1 — legacy JSON deserializes with every persisted field populated.
    // Fails if a [JsonPropertyName] pin is missing (field comes back null).
    // ---------------------------------------------------------------------
    [TestFixture]
    public class LegacySnapshotDeserializationTests
    {
        [Test]
        public void DriveGrant_LegacyJson_StorageKeyFieldPopulated()
        {
            var dg = SnapshotKeys.Load<DriveGrant>(LegacySnapshots.DriveGrant);
            Assert.That(dg.PermissionedDrive, Is.Not.Null);
            Assert.That(dg.KeyStoreKeyEncryptedStorageKey, Is.Not.Null,
                "KeyStoreKeyEncryptedStorageKey null — JsonPropertyName pin missing/incorrect");
        }

        [Test]
        public void ExchangeGrant_LegacyJson_AllCryptoFieldsPopulated()
        {
            var g = SnapshotKeys.Load<ExchangeGrant>(LegacySnapshots.ExchangeGrant);
            Assert.That(g.MasterKeyEncryptedKeyStoreKey, Is.Not.Null);
            Assert.That(g.KeyStoreKeyEncryptedIcrKey, Is.Not.Null);
            Assert.That(g.KeyStoreKeyEncryptedDriveGrants, Is.Not.Null.And.Not.Empty);
            Assert.That(g.KeyStoreKeyEncryptedDriveGrants[0].KeyStoreKeyEncryptedStorageKey, Is.Not.Null);
        }

        [Test]
        public void AccessRegistration_LegacyJson_AllCryptoFieldsPopulated()
        {
            var r = SnapshotKeys.Load<AccessRegistration>(LegacySnapshots.AccessRegistration);
            Assert.That(r.ClientAccessKeyEncryptedKeyStoreKey, Is.Not.Null);
            Assert.That(r.AccessKeyStoreKeyEncryptedSharedSecret, Is.Not.Null);
            Assert.That(r.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey, Is.Not.Null,
                "renamed → AppClientKeyEncryptedAppKey; pin missing if null");
        }

        [Test]
        public void AccessExchangeGrant_LegacyJson_AllCryptoFieldsPopulated()
        {
            var icr = SnapshotKeys.Load<AccessExchangeGrant>(LegacySnapshots.AccessExchangeGrant);
            Assert.That(icr.MasterKeyEncryptedKeyStoreKey, Is.Not.Null); // renamed → MasterKeyEncryptedPeerKey
            Assert.That(icr.AccessRegistration, Is.Not.Null);
            Assert.That(icr.AccessRegistration.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey, Is.Not.Null);
        }

        [Test]
        public void AppRegistration_LegacyJson_GrantGraphPopulated()
        {
            var a = SnapshotKeys.Load<AppRegistration>(LegacySnapshots.AppRegistration);
            Assert.That(a.Grant, Is.Not.Null);
            Assert.That(a.Grant.MasterKeyEncryptedKeyStoreKey, Is.Not.Null);
            Assert.That(a.Grant.KeyStoreKeyEncryptedDriveGrants[0].KeyStoreKeyEncryptedStorageKey, Is.Not.Null);
        }
    }

    // ---------------------------------------------------------------------
    // TIER 2 — wire names stay pinned to the legacy format.
    // ---------------------------------------------------------------------
    [TestFixture]
    public class WireNamePinningTests
    {
        private static void AssertWireStable(object fresh, string legacyJson)
        {
            var freshJson = OdinSystemSerializer.Serialize(fresh);

            // 1) every legacy top-level wire key is still emitted (no own-field rename leaked)
            Assert.That(WireCheck.TopLevelKeys(freshJson), Is.SupersetOf(WireCheck.TopLevelKeys(legacyJson)),
                "a persisted property name changed on the wire — missing JsonPropertyName pin");

            // 2) no new C# name leaked anywhere in the graph (nested included; case-insensitive)
            WireCheck.AssertNoForbiddenNames(freshJson);
        }

        [Test] public void DriveGrant_WireNamesStable()          => AssertWireStable(SnapshotFactory.DriveGrant(),          LegacySnapshots.DriveGrant);
        [Test] public void ExchangeGrant_WireNamesStable()       => AssertWireStable(SnapshotFactory.ExchangeGrant(),       LegacySnapshots.ExchangeGrant);
        [Test] public void AccessRegistration_WireNamesStable()  => AssertWireStable(SnapshotFactory.AccessRegistration(),  LegacySnapshots.AccessRegistration);
        [Test] public void AccessExchangeGrant_WireNamesStable() => AssertWireStable(SnapshotFactory.AccessExchangeGrant(), LegacySnapshots.AccessExchangeGrant);
        [Test] public void AppRegistration_WireNamesStable()     => AssertWireStable(SnapshotFactory.AppRegistration(),     LegacySnapshots.AppRegistration);
    }

    // ---------------------------------------------------------------------
    // TIER 3 — the bytes still decrypt to the expected keys.
    // ---------------------------------------------------------------------
    [TestFixture]
    public class LegacySnapshotDecryptionTests
    {
        [Test]
        public void DriveGrant_StorageKey_DecryptsWithAppKey()
        {
            var dg = SnapshotKeys.Load<DriveGrant>(LegacySnapshots.DriveGrant);
            var sk = dg.KeyStoreKeyEncryptedStorageKey.DecryptKeyClone(SnapshotKeys.AppKey);
            SnapshotKeys.AssertKey(sk, SnapshotKeys.StorageKey);
        }

        [Test]
        public void ExchangeGrant_AppKey_FromMasterKey_ThenUnlocksStorageKey()
        {
            var g = SnapshotKeys.Load<ExchangeGrant>(LegacySnapshots.ExchangeGrant);
            var appKey = g.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(SnapshotKeys.MasterKey);
            SnapshotKeys.AssertKey(appKey, SnapshotKeys.AppKey);

            var sk = g.KeyStoreKeyEncryptedDriveGrants[0].KeyStoreKeyEncryptedStorageKey.DecryptKeyClone(appKey);
            SnapshotKeys.AssertKey(sk, SnapshotKeys.StorageKey);
        }

        [Test]
        public void AccessExchangeGrant_PeerKey_FromMasterKey()
        {
            var icr = SnapshotKeys.Load<AccessExchangeGrant>(LegacySnapshots.AccessExchangeGrant);
            var peerKey = icr.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(SnapshotKeys.MasterKey);
            SnapshotKeys.AssertKey(peerKey, SnapshotKeys.PeerKey);
        }

        [Test]
        public void AccessRegistration_AppKey_ViaAccessKeyStoreKey()
        {
            var r = SnapshotKeys.Load<AccessRegistration>(LegacySnapshots.AccessRegistration);
            // decrypt the AES-wrapped field directly with the known wrapping key;
            // the blob omits the client token-half, so we skip the full XOR/CAT path.
            var appKey = r.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey.DecryptKeyClone(SnapshotKeys.AccessKeyStoreKey);
            SnapshotKeys.AssertKey(appKey, SnapshotKeys.AppKey);
        }
    }

    // ---------------------------------------------------------------------
    // Shared wire-format checks (used by the factory and real-flow suites).
    // ---------------------------------------------------------------------
    internal static class WireCheck
    {
        // C# names introduced by the rename that must NEVER reach the wire (anywhere in the graph).
        public static readonly string[] ForbiddenNewNames =
        {
            "MasterKeyEncryptedAppKey", "MasterKeyEncryptedPeerKey",
            "AppClientKeyEncryptedAppKey", "AppKeyEncryptedStorageKey", "PeerKeyEncryptedStorageKey"
        };

        public static HashSet<string> TopLevelKeys(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        }

        // The wire names System.Text.Json would emit for T's own top-level properties,
        // honoring [JsonPropertyName] (the rename's pins) and [JsonIgnore], else camelCase.
        public static HashSet<string> ExpectedWireNames(Type t)
        {
            var names = new HashSet<string>();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead) continue;
                if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                var pinned = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
                names.Add(pinned ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name));
            }
            return names;
        }

        public static void AssertNoForbiddenNames(string json)
        {
            foreach (var bad in ForbiddenNewNames)
                Assert.That(json, Does.Not.Contain(bad).IgnoreCase, $"emitted renamed symbol '{bad}' onto the wire");
        }

        // Deserialize the frozen snapshot, re-serialize, and assert the wire stayed stable:
        // no persisted key dropped, no renamed symbol leaked, and serialization is a fixed point.
        public static void AssertRoundTripStable<T>(string snapshot)
        {
            var rt = OdinSystemSerializer.Serialize(OdinSystemSerializer.Deserialize<T>(snapshot));
            Assert.That(TopLevelKeys(rt), Is.SupersetOf(TopLevelKeys(snapshot)),
                $"{typeof(T).Name}: a persisted property name changed on the wire — missing JsonPropertyName pin");
            AssertNoForbiddenNames(rt);
            var rt2 = OdinSystemSerializer.Serialize(OdinSystemSerializer.Deserialize<T>(rt));
            Assert.That(rt2, Is.EqualTo(rt), $"{typeof(T).Name}: serialization is not idempotent");
        }

        // Every persisted top-level property of T appears in the snapshot — proves the
        // fixture actually exercises each pin-needing field (no silently-absent property).
        public static void AssertCompleteness<T>(string snapshot)
        {
            var expected = ExpectedWireNames(typeof(T));
            Assert.That(expected, Is.Not.Empty, $"{typeof(T).Name}: reflection found no persisted properties — check guard");
            Assert.That(TopLevelKeys(snapshot), Is.SupersetOf(expected),
                $"{typeof(T).Name}: snapshot is missing a persisted property — fixture does not cover it");
        }
    }

    // ---------------------------------------------------------------------
    // REAL on-the-wire blobs captured from a live provision via
    // Odin.Hosting.Tests RealBlobCaptureTest (KeyThreeValue.data / Connections.data).
    // Frozen pre-rename. These are the genuine production shapes — note the connection
    // persists as IcrAccessRecord with circleGrants/appGrants cleared to {} (stored in
    // sibling tables), which a hand-built fixture would not capture.
    // ---------------------------------------------------------------------
    internal static class RealSnapshots
    {
        public const string AppRegistration = """{"appId":"59e73f4c16544946beef47a948b3677e","name":"Test_59e73f4c-1654-4946-beef-47a948b3677e","authorizedCircles":["99f63c2c-1778-464e-8cb2-8359c347ec4d"],"circleMemberPermissionGrant":{"drives":[{"permissionedDrive":{"drive":{"alias":"cb8885a774b54f519d5a9441cc69f470","type":"61bdb80a3f0b4a76a3b06f836e779201"},"permission":"read","temporalReadWindowSeconds":null}}],"permissionSet":{"keys":[10]}},"grant":{"created":1782523571515,"modified":0,"masterKeyEncryptedKeyStoreKey":{"keyEncrypted":"+HDaeCH9V4oP8fnLtN4dRbz2f2PzaWd5fAUsOLIZ70Y=","keyIV":"XtBEjA2D/4shkCU6tGMVuw==","keyHash":"qFEOnxl7sA9rDFp8yCHOQQ=="},"isRevoked":false,"keyStoreKeyEncryptedDriveGrants":[{"driveId":"cb8885a7-74b5-4f51-9d5a-9441cc69f470","permissionedDrive":{"drive":{"alias":"cb8885a774b54f519d5a9441cc69f470","type":"61bdb80a3f0b4a76a3b06f836e779201"},"permission":"readWrite","temporalReadWindowSeconds":null},"keyStoreKeyEncryptedStorageKey":{"keyEncrypted":"ejj8z6XWN4SLXnC2wFlAjzgsbTjAHPG2hUM6TmjTCcw=","keyIV":"ZWXvOWM8/HWM3rYevq8NGw==","keyHash":"ZpWi1BZEZ3v3Vbr+FVY89A=="}}],"permissionSet":{"keys":[10,50]},"keyStoreKeyEncryptedIcrKey":null},"corsHostName":null}""";

        public const string IcrAccessRecord = """{"accessGrant":{"masterKeyEncryptedKeyStoreKey":{"keyEncrypted":"I7oZktlglfJ6WEnxLTqD8PjXt6pcYxMDQeX+2zPOuQI=","keyIV":"kHGIPgDVs/aTqeeDQlD6xw==","keyHash":"ZvDCLRQt/HLZNZjFPhIhPQ=="},"circleGrants":{},"appGrants":{},"accessRegistration":{"id":"f36af019400bae009d36a622d1fd4121","accessRegistrationClientType":"other","created":1782523572404,"clientAccessKeyEncryptedKeyStoreKey":{"keyEncrypted":"4O+AMNTYVt5Ira1u8D/U6A==","keyHash":"rBd9pHRUIRlikhmRUIip1A=="},"accessKeyStoreKeyEncryptedSharedSecret":{"keyEncrypted":"p26mNtFHN00xURihw6ASKNWwDptmWdelvVhImIbhBb8=","keyIV":"R4Ldamwtux9naLxwEqBwAQ==","keyHash":"7iZiQHJQcfvDxG4F+KgF2A=="},"isRevoked":false,"accessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey":{"keyEncrypted":"Xiua4R63nY9eG+RalQ8drSs6s63wykF/R7SKiRVEQYg=","keyIV":"ZXZaNlEFvMLRTQnF94KKYg==","keyHash":"zNLlHE94diZ14duwHYr/uw=="}},"isRevoked":false},"encryptedClientAccessToken":{"keyEncrypted":"up5GoQWMgQRy+ZHtlnjIgLvSQjIqSqe4fwYbDfR80Fsn+O+UZzElWEr284XH+Akqq4c9yHQ1I/g+u9WA8PYuFA==","keyIV":"2OqBcMocFnRfqFim6+M9tw==","keyHash":"4sLdyfZ30KP8qBW7ckYIQg=="},"weakClientAccessToken":"","weakKeyStoreKey":"","originalContactData":{"name":"Test Test","imageId":"00000000-0000-0000-0000-000000000000","contact":null},"introducerOdinId":null,"verificationHash64":"UwVMIcaTzmtSSYzeHk48X9mSAyquGS3AAXDR4d6NxMk=","connectionOrigin":"IdentityOwner"}""";
    }

    // ---------------------------------------------------------------------
    // Real-flow compatibility: the captured production blobs deserialize, stay
    // wire-stable across a round trip, and are covered field-for-field.
    // ---------------------------------------------------------------------
    [TestFixture]
    public class RealSnapshotCompatibilityTests
    {
        // tier 1 — the renamed persisted fields still bind (non-null) on real data
        [Test]
        public void AppRegistration_RenamedFieldsPopulated()
        {
            var a = OdinSystemSerializer.Deserialize<AppRegistration>(RealSnapshots.AppRegistration);
            Assert.That(a.Grant, Is.Not.Null);
            Assert.That(a.Grant.MasterKeyEncryptedKeyStoreKey, Is.Not.Null);                 // -> MasterKeyEncryptedAppKey
            Assert.That(a.Grant.KeyStoreKeyEncryptedDriveGrants, Is.Not.Null.And.Not.Empty);
            Assert.That(a.Grant.KeyStoreKeyEncryptedDriveGrants[0].KeyStoreKeyEncryptedStorageKey, Is.Not.Null); // -> *KeyEncryptedStorageKey
        }

        [Test]
        public void IcrAccessRecord_RenamedFieldsPopulated()
        {
            var icr = OdinSystemSerializer.Deserialize<IcrAccessRecord>(RealSnapshots.IcrAccessRecord);
            Assert.That(icr.AccessGrant, Is.Not.Null);
            Assert.That(icr.AccessGrant.MasterKeyEncryptedKeyStoreKey, Is.Not.Null);          // -> MasterKeyEncryptedPeerKey
            Assert.That(icr.AccessGrant.AccessRegistration, Is.Not.Null);
            Assert.That(icr.AccessGrant.AccessRegistration.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey, Is.Not.Null); // -> AppClientKeyEncryptedAppKey
        }

        // tier 2 + idempotence — wire stays pinned across a round trip
        [Test] public void AppRegistration_WireStable() => WireCheck.AssertRoundTripStable<AppRegistration>(RealSnapshots.AppRegistration);
        [Test] public void IcrAccessRecord_WireStable() => WireCheck.AssertRoundTripStable<IcrAccessRecord>(RealSnapshots.IcrAccessRecord);

        // completeness — the fixture covers every persisted top-level property of the type
        [Test] public void AppRegistration_Complete() => WireCheck.AssertCompleteness<AppRegistration>(RealSnapshots.AppRegistration);
        [Test] public void IcrAccessRecord_Complete() => WireCheck.AssertCompleteness<IcrAccessRecord>(RealSnapshots.IcrAccessRecord);
    }
}
