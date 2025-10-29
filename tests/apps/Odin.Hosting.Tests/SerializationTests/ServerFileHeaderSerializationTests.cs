using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.SerializationTests
{
    [TestFixture]
    public class ServerFileHeaderSerializationTests
    {
        private JsonSerializerOptions _json;

        [SetUp]
        public void SetUp()
        {
            _json = new JsonSerializerOptions
            {
                WriteIndented = false,
                IncludeFields = true, // be explicit: include fields if any exist
                DefaultIgnoreCondition = JsonIgnoreCondition.Never, // do not exclude ANY fields
                IgnoreReadOnlyProperties = false,
                IgnoreReadOnlyFields = false
            };
            // UnixTime converters are applied via attributes on the structs.
        }

        [Test]
        public void UnixTimeUtc_Serializes_As_Number()
        {
            var now = UnixTimeUtc.Now();
            var json = JsonSerializer.Serialize(now, _json);

            ClassicAssert.IsTrue(json.Trim().All(char.IsDigit),
                "UnixTimeUtc should serialize as a JSON number (milliseconds). Got: " + json);

            var roundTrip = JsonSerializer.Deserialize<UnixTimeUtc>(json, _json);
            ClassicAssert.AreEqual((long)now, (long)roundTrip);
        }

        [Test]
        public void UnixTimeUtcUnique_Serializes_As_Number()
        {
            var unique = UnixTimeUtcUnique.Now();
            var json = JsonSerializer.Serialize(unique, _json);

            ClassicAssert.IsTrue(json.Trim().All(char.IsDigit),
                "UnixTimeUtcUnique should serialize as a JSON number (uniqueTime). Got: " + json);

            var roundTrip = JsonSerializer.Deserialize<UnixTimeUtcUnique>(json, _json);
            ClassicAssert.AreEqual(unique.uniqueTime, roundTrip.uniqueTime);
        }

        [Test]
        public void ReactionSummary_RoundTrips_And_Equals_By_ObjectGraph()
        {
            var rcp = new ReactionContentPreview
            {
                Key = Guid.NewGuid(),
                ReactionContent = "👍",
                Count = 3
            };

            var comment = new CommentPreview
            {
                FileId = Guid.NewGuid(),
                OdinId = "example.com",
                Content = "Hello world",
                Reactions = new List<ReactionContentPreview>
                {
                    new ReactionContentPreview { Key = Guid.NewGuid(), ReactionContent = "❤️", Count = 2 }
                },
                Created = UnixTimeUtc.Now(),
                Updated = UnixTimeUtc.Now().AddSeconds(5),
                IsEncrypted = true
            };

            var sut = new ReactionSummary
            {
                Reactions = new Dictionary<Guid, ReactionContentPreview>
                {
                    { Guid.NewGuid(), rcp }
                },
                Comments = new List<CommentPreview> { comment },
                TotalCommentCount = 1
            };

            var json = JsonSerializer.Serialize(sut, _json);
            ClassicAssert.IsNotNull(json);
            ClassicAssert.IsTrue(json.Length > 0);

            var rt = JsonSerializer.Deserialize<ReactionSummary>(json, _json);
            ClassicAssert.IsNotNull(rt);

            AssertReactionSummaryEqual(sut, rt!);
        }

        [Test]
        public void ServerFileHeader_Serializes_All_Fields_And_Equals_By_ObjectGraph()
        {
            // --- Build EncryptedKeyHeader
            var ekh = new EncryptedKeyHeader
            {
                EncryptionVersion = 1,
                Type = EncryptionType.Aes,
                Iv = Guid.NewGuid().ToByteArray(),
                EncryptedAesKey = ByteArray(48)
            };

            // --- Build FileMetadata (populate broadly; rely on project models)
            var fileId = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.NewGuid() };

            var payload = new PayloadDescriptor
            {
                Iv = ByteArray(16),
                Key = "main",
                ContentType = "application/octet-stream",
                BytesWritten = 12345,
                LastModified = UnixTimeUtc.Now(),
                DescriptorContent = "desc",
                Uid = UnixTimeUtcUnique.Now(),
                PreviewThumbnail = new ThumbnailContent
                {
                    PixelWidth = 64,
                    PixelHeight = 64,
                    ContentType = "image/png",
                    BytesWritten = 512,
                    Content = ByteArray(256)
                },
                Thumbnails =
                [
                    new ThumbnailDescriptor
                    {
                        PixelWidth = 32,
                        PixelHeight = 32,
                        ContentType = "image/png",
                        BytesWritten = 384
                    }
                ]
            };

            var localApp = new LocalAppMetadata
            {
                VersionTag = Guid.NewGuid(),
                Iv = ByteArray(16),
                Content = "local-meta",
                Tags = [Guid.NewGuid(), Guid.NewGuid()]
            };

            // fileType / dataType are INTs by type
            var appMeta = new AppFileMetaData
            {
                UniqueId = Guid.NewGuid(),
                GroupId = Guid.NewGuid(),
                FileType = 7, // int
                DataType = 9, // int
                ArchivalStatus = 0,
                UserDate = UnixTimeUtc.Now(),
                Content = "ABC123",
                PreviewThumbnail = null,
                Tags = [Guid.NewGuid(), Guid.NewGuid()],
            };

            var fm = new FileMetadata(fileId)
            {
                ReferencedFile = new GlobalTransitIdFileIdentifier
                {
                    TargetDrive = TargetDrive.NewTargetDrive(),
                    GlobalTransitId = Guid.NewGuid()
                },
                GlobalTransitId = Guid.NewGuid(),
                FileState = FileState.Active,
                TransitCreated = UnixTimeUtc.Now(),
                TransitUpdated = UnixTimeUtc.Now().AddSeconds(30),
                ReactionPreview = new ReactionSummary
                {
                    TotalCommentCount = 0,
                    Reactions = new Dictionary<Guid, ReactionContentPreview>(),
                    Comments = new List<CommentPreview>()
                },
                IsEncrypted = true,
                SenderOdinId = "sender.example.com",
                OriginalAuthor = new OdinId("author.example.com"),
                AppData = appMeta,
                LocalAppData = localApp,
                Payloads = [payload],
                VersionTag = Guid.NewGuid(),
                DataSource = new DataSource
                {
                    DriveId = Guid.NewGuid(),
                    Identity = new OdinId("source.example.com"),
                    PayloadsAreRemote = false
                }
            };

            // Explicit created/updated
            fm.SetCreatedModifiedWithDatabaseValue(UnixTimeUtc.Now(), UnixTimeUtc.Now());

            // --- Build ServerMetadata
            var acl = new AccessControlList
            {
                RequiredSecurityGroup = SecurityGroupType.Owner
            };

            var sm = new ServerMetadata
            {
                AccessControlList = acl,
                AllowDistribution = true,
                FileSystemType = FileSystemType.Standard,
                FileByteCount = 654321,
                OriginalRecipientCount = 2,
                TransferHistory = new RecipientTransferHistory
                {
                    Summary = new TransferHistorySummary
                    {
                        TotalInOutbox = 1,
                        TotalDelivered = 10,
                        TotalFailed = 0,
                        TotalReadByRecipient = 7
                    }
                }
            };

            // --- Compose header
            var header = new ServerFileHeader
            {
                EncryptedKeyHeader = ekh,
                FileMetadata = fm,
                ServerMetadata = sm
            };

            // Serialize / Deserialize
            var json = OdinSystemSerializer.Serialize(header);
            ClassicAssert.IsNotNull(json);
            ClassicAssert.IsTrue(json.Length > 0);

            var roundTrip = OdinSystemSerializer.Deserialize<ServerFileHeader>(json);
            ClassicAssert.IsNotNull(roundTrip);
            ClassicAssert.IsTrue(roundTrip!.IsValid(), "Round-tripped header should be valid.");

            // Deep object comparisons (no JSON parsing)
            AssertServerFileHeaderEqual(header, roundTrip);

            // Extra sanity: int types stayed ints
            ClassicAssert.AreEqual(header.FileMetadata.AppData.FileType, roundTrip.FileMetadata.AppData.FileType);
            ClassicAssert.AreEqual(header.FileMetadata.AppData.DataType, roundTrip.FileMetadata.AppData.DataType);
        }

        // --- helpers (deep comparison) ---------------------------------------

        private static void AssertServerFileHeaderEqual(ServerFileHeader expected, ServerFileHeader actual)
        {
            ClassicAssert.IsNotNull(expected);
            ClassicAssert.IsNotNull(actual);

            AssertEncryptedKeyHeaderEqual(expected.EncryptedKeyHeader, actual.EncryptedKeyHeader);
            AssertFileMetadataEqual(expected.FileMetadata, actual.FileMetadata);
            AssertServerMetadataEqual(expected.ServerMetadata, actual.ServerMetadata);
        }

        private static void AssertEncryptedKeyHeaderEqual(EncryptedKeyHeader e, EncryptedKeyHeader a)
        {
            ClassicAssert.IsNotNull(e);
            ClassicAssert.IsNotNull(a);

            ClassicAssert.AreEqual(e!.EncryptionVersion, a!.EncryptionVersion);
            ClassicAssert.AreEqual(e.Type, a.Type);
            AssertByteArrayEqual(e.Iv, a.Iv);
            AssertByteArrayEqual(e.EncryptedAesKey, a.EncryptedAesKey);
        }

        private static void AssertFileMetadataEqual(FileMetadata e, FileMetadata a)
        {
            ClassicAssert.IsNotNull(e);
            ClassicAssert.IsNotNull(a);

            // ReferencedFile
            if (e.ReferencedFile is null)
            {
                ClassicAssert.IsNull(a.ReferencedFile);
            }
            else
            {
                ClassicAssert.IsNotNull(a.ReferencedFile);
                ClassicAssert.AreEqual(e.ReferencedFile.GlobalTransitId, a.ReferencedFile.GlobalTransitId);
                ClassicAssert.AreEqual(e.ReferencedFile.TargetDrive, a.ReferencedFile.TargetDrive);
            }

            // File ids
            ClassicAssert.AreEqual(e.File.DriveId, a.File.DriveId);
            ClassicAssert.AreEqual(e.File.FileId, a.File.FileId);

            ClassicAssert.AreEqual(e.GlobalTransitId, a.GlobalTransitId);
            ClassicAssert.AreEqual(e.FileState, a.FileState);
            ClassicAssert.AreEqual((long)e.Created, (long)a.Created);
            ClassicAssert.AreEqual((long)e.Updated, (long)a.Updated);
            ClassicAssert.AreEqual((long)e.TransitCreated, (long)a.TransitCreated);
            ClassicAssert.AreEqual((long)e.TransitUpdated, (long)a.TransitUpdated);

            // ReactionPreview
            if (e.ReactionPreview is null)
                ClassicAssert.IsNull(a.ReactionPreview);
            else
                AssertReactionSummaryEqual(e.ReactionPreview, a.ReactionPreview!);

            ClassicAssert.AreEqual(e.IsEncrypted, a.IsEncrypted);
            ClassicAssert.AreEqual(e.SenderOdinId, a.SenderOdinId);
            ClassicAssert.AreEqual(e.OriginalAuthor?.ToString(), a.OriginalAuthor?.ToString());

            // AppData
            AssertAppFileMetaDataEqual(e.AppData, a.AppData);

            // LocalAppData
            if (e.LocalAppData is null)
                ClassicAssert.IsNull(a.LocalAppData);
            else
                AssertLocalAppMetadataEqual(e.LocalAppData, a.LocalAppData!);

            // Payloads
            if (e.Payloads is null)
            {
                ClassicAssert.IsNull(a.Payloads);
            }
            else
            {
                ClassicAssert.IsNotNull(a.Payloads);
                ClassicAssert.AreEqual(e.Payloads.Count, a.Payloads!.Count);
                for (int i = 0; i < e.Payloads.Count; i++)
                    AssertPayloadDescriptorEqual(e.Payloads[i], a.Payloads[i]);
            }

            ClassicAssert.AreEqual(e.VersionTag, a.VersionTag);

            // DataSource
            if (e.DataSource is null)
            {
                ClassicAssert.IsNull(a.DataSource);
            }
            else
            {
                ClassicAssert.IsNotNull(a.DataSource);
                ClassicAssert.AreEqual(e.DataSource.DriveId, a.DataSource.DriveId);
                ClassicAssert.AreEqual(e.DataSource.Identity.ToString(), a.DataSource.Identity.ToString());
                ClassicAssert.AreEqual(e.DataSource.PayloadsAreRemote, a.DataSource.PayloadsAreRemote);
            }
        }

        private static void AssertServerMetadataEqual(Services.Drives.DriveCore.Storage.ServerMetadata e,
                                                      Services.Drives.DriveCore.Storage.ServerMetadata a)
        {
            ClassicAssert.IsNotNull(e);
            ClassicAssert.IsNotNull(a);

            // ACL
            if (e.AccessControlList is null)
                ClassicAssert.IsNull(a.AccessControlList);
            else
                ClassicAssert.AreEqual(e.AccessControlList.RequiredSecurityGroup, a.AccessControlList!.RequiredSecurityGroup);

            ClassicAssert.AreEqual(e.AllowDistribution, a.AllowDistribution);
            ClassicAssert.AreEqual(e.FileSystemType, a.FileSystemType);
            ClassicAssert.AreEqual(e.FileByteCount, a.FileByteCount);
            ClassicAssert.AreEqual(e.OriginalRecipientCount, a.OriginalRecipientCount);

            // TransferHistory
            if (e.TransferHistory is null)
            {
                ClassicAssert.IsNull(a.TransferHistory);
            }
            else
            {
                ClassicAssert.IsNotNull(a.TransferHistory);
                if (e.TransferHistory.Summary is null)
                {
                    ClassicAssert.IsNull(a.TransferHistory!.Summary);
                }
                else
                {
                    ClassicAssert.IsNotNull(a.TransferHistory!.Summary);
                    ClassicAssert.AreEqual(e.TransferHistory.Summary.TotalInOutbox, a.TransferHistory.Summary!.TotalInOutbox);
                    ClassicAssert.AreEqual(e.TransferHistory.Summary.TotalDelivered, a.TransferHistory.Summary.TotalDelivered);
                    ClassicAssert.AreEqual(e.TransferHistory.Summary.TotalFailed, a.TransferHistory.Summary.TotalFailed);
                    ClassicAssert.AreEqual(e.TransferHistory.Summary.TotalReadByRecipient, a.TransferHistory.Summary.TotalReadByRecipient);
                }
            }
        }

        private static void AssertAppFileMetaDataEqual(AppFileMetaData e, AppFileMetaData a)
        {
            ClassicAssert.IsNotNull(e);
            ClassicAssert.IsNotNull(a);

            ClassicAssert.AreEqual(e.UniqueId, a.UniqueId);
            ClassicAssert.AreEqual(e.GroupId, a.GroupId);

            // Critical: these are INTs and should round-trip exactly
            ClassicAssert.AreEqual(e.FileType, a.FileType);
            ClassicAssert.AreEqual(e.DataType, a.DataType);

            ClassicAssert.AreEqual(e.ArchivalStatus, a.ArchivalStatus);
            ClassicAssert.AreEqual((long)(e.UserDate ?? UnixTimeUtc.ZeroTime), (long)(a.UserDate ?? UnixTimeUtc.ZeroTime));

            ClassicAssert.AreEqual(e.Content, a.Content);
            if (e.PreviewThumbnail is null)
                ClassicAssert.IsNull(a.PreviewThumbnail);
            else
                AssertThumbnailDescriptorEqual(e.PreviewThumbnail, a.PreviewThumbnail!);

            // Tags
            if (e.Tags is null) ClassicAssert.IsNull(a.Tags);
            else
            {
                ClassicAssert.IsNotNull(a.Tags);
                ClassicAssert.AreEqual(e.Tags!.Count, a.Tags!.Count);
                for (int i = 0; i < e.Tags!.Count; i++)
                    ClassicAssert.AreEqual(e.Tags[i], a.Tags![i]);
            }
        }

        private static void AssertLocalAppMetadataEqual(LocalAppMetadata e, LocalAppMetadata a)
        {
            ClassicAssert.IsNotNull(e);
            ClassicAssert.IsNotNull(a);

            ClassicAssert.AreEqual(e.VersionTag, a.VersionTag);
            AssertByteArrayEqual(e.Iv, a.Iv);
            ClassicAssert.AreEqual(e.Content, a.Content);

            if (e.Tags is null) ClassicAssert.IsNull(a.Tags);
            else
            {
                ClassicAssert.IsNotNull(a.Tags);
                ClassicAssert.AreEqual(e.Tags!.Count, a.Tags!.Count);
                for (int i = 0; i < e.Tags!.Count; i++)
                    ClassicAssert.AreEqual(e.Tags[i], a.Tags![i]);
            }
        }

        private static void AssertPayloadDescriptorEqual(PayloadDescriptor e, PayloadDescriptor a)
        {
            ClassicAssert.IsNotNull(e);
            ClassicAssert.IsNotNull(a);

            AssertByteArrayEqual(e.Iv, a.Iv);
            ClassicAssert.AreEqual(e.Key, a.Key);
            ClassicAssert.AreEqual(e.ContentType, a.ContentType);
            ClassicAssert.AreEqual(e.BytesWritten, a.BytesWritten);
            ClassicAssert.AreEqual((long)e.LastModified, (long)a.LastModified);
            ClassicAssert.AreEqual(e.DescriptorContent, a.DescriptorContent);
            ClassicAssert.AreEqual(e.Uid.uniqueTime, a.Uid.uniqueTime);

            // PreviewThumbnail
            if (e.PreviewThumbnail is null)
                ClassicAssert.IsNull(a.PreviewThumbnail);
            else
                AssertThumbnailContentEqual(e.PreviewThumbnail, a.PreviewThumbnail!);

            // Thumbnails
            if (e.Thumbnails is null)
            {
                ClassicAssert.IsNull(a.Thumbnails);
            }
            else
            {
                ClassicAssert.IsNotNull(a.Thumbnails);
                ClassicAssert.AreEqual(e.Thumbnails.Count, a.Thumbnails!.Count);
                for (int i = 0; i < e.Thumbnails.Count; i++)
                {
                    AssertThumbnailDescriptorEqual(e.Thumbnails[i], a.Thumbnails[i]);
                }
            }
        }

        private static void AssertReactionSummaryEqual(ReactionSummary e, ReactionSummary a)
        {
            ClassicAssert.IsNotNull(e);
            ClassicAssert.IsNotNull(a);

            ClassicAssert.AreEqual(e.TotalCommentCount, a.TotalCommentCount);

            // Reactions (Dictionary<Guid, ReactionContentPreview>)
            if (e.Reactions is null)
            {
                ClassicAssert.IsNull(a.Reactions);
            }
            else
            {
                ClassicAssert.IsNotNull(a.Reactions);
                ClassicAssert.AreEqual(e.Reactions.Count, a.Reactions!.Count);
                foreach (var kv in e.Reactions)
                {
                    ClassicAssert.IsTrue(a.Reactions.ContainsKey(kv.Key), "Missing reaction key: " + kv.Key);
                    AssertReactionContentPreviewEqual(kv.Value, a.Reactions[kv.Key]);
                }
            }

            // Comments
            if (e.Comments is null)
            {
                ClassicAssert.IsNull(a.Comments);
            }
            else
            {
                ClassicAssert.IsNotNull(a.Comments);
                ClassicAssert.AreEqual(e.Comments.Count, a.Comments!.Count);
                for (int i = 0; i < e.Comments.Count; i++)
                    AssertCommentPreviewEqual(e.Comments[i], a.Comments[i]);
            }
        }

        private static void AssertReactionContentPreviewEqual(ReactionContentPreview e, ReactionContentPreview a)
        {
            ClassicAssert.IsNotNull(e);
            ClassicAssert.IsNotNull(a);

            ClassicAssert.AreEqual(e.Key, a.Key);
            ClassicAssert.AreEqual(e.ReactionContent, a.ReactionContent);
            ClassicAssert.AreEqual(e.Count, a.Count);
        }

        private static void AssertCommentPreviewEqual(CommentPreview e, CommentPreview a)
        {
            ClassicAssert.IsNotNull(e);
            ClassicAssert.IsNotNull(a);

            ClassicAssert.AreEqual(e.FileId, a.FileId);
            ClassicAssert.AreEqual(e.OdinId, a.OdinId);
            ClassicAssert.AreEqual(e.Content, a.Content);

            // Reactions list
            if (e.Reactions is null)
            {
                ClassicAssert.IsNull(a.Reactions);
            }
            else
            {
                ClassicAssert.IsNotNull(a.Reactions);
                ClassicAssert.AreEqual(e.Reactions.Count, a.Reactions!.Count);
                for (int i = 0; i < e.Reactions.Count; i++)
                    AssertReactionContentPreviewEqual(e.Reactions[i], a.Reactions[i]);
            }

            ClassicAssert.AreEqual((long)e.Created, (long)a.Created);
            ClassicAssert.AreEqual((long)e.Updated, (long)a.Updated);
            ClassicAssert.AreEqual(e.IsEncrypted, a.IsEncrypted);
        }

        private static void AssertThumbnailDescriptorEqual(ThumbnailDescriptor e, ThumbnailDescriptor a)
        {
            ClassicAssert.IsNotNull(e);
            ClassicAssert.IsNotNull(a);

            ClassicAssert.AreEqual(e.PixelWidth, a.PixelWidth);
            ClassicAssert.AreEqual(e.PixelHeight, a.PixelHeight);
            ClassicAssert.AreEqual(e.ContentType, a.ContentType);
            ClassicAssert.AreEqual(e.BytesWritten, a.BytesWritten);
        }

        private static void AssertThumbnailContentEqual(ThumbnailContent e, ThumbnailContent a)
        {
            AssertThumbnailDescriptorEqual(e, a);
            AssertByteArrayEqual(e.Content, a.Content);
        }

        private static void AssertByteArrayEqual(byte[] e, byte[] a)
        {
            if (e is null) { ClassicAssert.IsNull(a); return; }
            ClassicAssert.IsNotNull(a);
            ClassicAssert.AreEqual(e.Length, a!.Length);
            for (int i = 0; i < e.Length; i++)
                ClassicAssert.AreEqual(e[i], a[i], $"Byte mismatch at index {i}");
        }

        private static byte[] ByteArray(int len)
        {
            var b = new byte[len];
            new Random(1234).NextBytes(b);
            return b;
        }
    }
}
