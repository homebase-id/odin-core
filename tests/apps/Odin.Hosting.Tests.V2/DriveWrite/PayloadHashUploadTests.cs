using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// Client-computed payload hashes on upload, update and add-payload (#1895, spec in docs/payload-hashes.md):
/// the server verifies storedHash as the bytes stream in, stores the hash unchanged, rejects a mismatch or a
/// malformed hash without leaving files behind, and enforces the drive's requirePayloadHashes setting.
/// </summary>
[TestFixture]
public class PayloadHashUploadTests : V2Fixture
{
    //
    // Create
    //

    [TestCase(ContentHashAlgorithm.Sha256)]
    [TestCase(ContentHashAlgorithm.Blake3)]
    public async Task UnencryptedUploadStoresTheHash(ContentHashAlgorithm algorithm)
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var payload = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), algorithm);
        var result = await CreateUnencrypted(owner, spec.TargetDrive, payload);

        var stored = await StoredDescriptor(owner, result, payload.Key);
        AssertSameHash(stored.Hash, payload.Hash);
    }

    [TestCase(ContentHashAlgorithm.Sha256)]
    [TestCase(ContentHashAlgorithm.Blake3)]
    public async Task EncryptedUploadStoresTheHash(ContentHashAlgorithm algorithm)
    {
        var spec = CallerSpec.Owner(DriveSpec.Secured());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var keyHeader = KeyHeader.NewRandom16();
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        payload.Iv = ByteArrayUtil.GetRndByteArray(16);
        payload.Hash = EncryptedHash(payload, keyHeader, algorithm);

        var metadata = SampleMetadataData.CreateWithContent(fileType: 100, "encrypted app content", AccessControlList.OwnerOnly);
        var manifest = new UploadManifest { PayloadDescriptors = new[] { payload }.ToPayloadDescriptorList().ToList() };
        var (response, _, _, _) = await owner.Drives.Writer.CreateEncryptedFile(spec.TargetDrive.Alias, metadata,
            transitOptions: null, manifest, [payload], keyHeader: keyHeader);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"actual {response.StatusCode}");

        var stored = await StoredDescriptor(owner, response.Content!, payload.Key);
        AssertSameHash(stored.Hash, payload.Hash);
        Assert.That(stored.Hash.ContentHash.Length, Is.EqualTo(PayloadHash.EncryptedContentHashLength));

        // The client can decrypt the content hash with the file key and the derived IV
        var decrypted = AesCbc.Decrypt(stored.Hash.ContentHash, keyHeader.AesKey.GetKey(), PayloadHash.ContentHashIv(payload.Iv));
        Assert.That(decrypted, Is.EqualTo(IncrementalContentHash.Compute(algorithm, payload.Content)));
    }

    [Test]
    public async Task UploadWithoutAHashIsUnchanged()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var result = await CreateUnencrypted(owner, spec.TargetDrive, payload);

        Assert.That((await StoredDescriptor(owner, result, payload.Key)).Hash, Is.Null);
    }

    [Test]
    public async Task AWrongStoredHashIsRejectedAndLeavesNoFile()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var payload = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Blake3);
        payload.Content = "not the bytes that were hashed".ToUtf8ByteArray();

        await AssertCreateRejected(owner, spec.TargetDrive, payload, OdinClientErrorCode.PayloadHashMismatch);
    }

    [Test]
    public async Task AnIncompleteHashIsRejected()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var payload = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Sha256);
        payload.Hash.ContentHash = null;

        await AssertCreateRejected(owner, spec.TargetDrive, payload, OdinClientErrorCode.InvalidPayloadHash);
    }

    [Test]
    public async Task AnUnencryptedContentHashThatDiffersFromTheStoredHashIsRejected()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var payload = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Sha256);
        payload.Hash.ContentHash = IncrementalContentHash.Compute(ContentHashAlgorithm.Sha256, "something else"u8);

        await AssertCreateRejected(owner, spec.TargetDrive, payload, OdinClientErrorCode.InvalidPayloadHash);
    }

    //
    // Update
    //

    [Test]
    public async Task UpdateSetsKeepsAndClearsHashesPerPayload()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var kept = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Blake3);
        var cleared = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition2(), ContentHashAlgorithm.Blake3);
        var seedMetadata = SampleMetadataData.Create(fileType: 100);
        var seed = await owner.Drives.Writer.CreateNewUnencryptedFile(spec.TargetDrive.Alias, seedMetadata,
            new UploadManifest { PayloadDescriptors = new[] { kept, cleared }.ToPayloadDescriptorList().ToList() }, [kept, cleared]);
        Assert.That(seed.IsSuccessStatusCode, Is.True, $"actual {seed.StatusCode}");
        var file = seed.Content!;

        // Overwrite `cleared` without a hash, add a new payload with a SHA-256 hash, leave `kept` untouched
        var replacement = SamplePayloadDefinitions.GetPayloadDefinition2();
        replacement.Content = "replacement content without a hash".ToUtf8ByteArray();
        var added = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(), ContentHashAlgorithm.Sha256);

        seedMetadata.VersionTag = file.NewVersionTag;
        var update = await owner.Drives.Writer.UpdateFileByFileId(file.DriveId, file.FileId, UpdateInstructions(
                replacement.ToPayloadDescriptor(PayloadUpdateOperationType.AppendOrOverwrite),
                added.ToPayloadDescriptor(PayloadUpdateOperationType.AppendOrOverwrite)),
            seedMetadata, [replacement, added]);
        Assert.That(update.IsSuccessStatusCode, Is.True, $"actual {update.StatusCode}");

        var payloads = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!.FileMetadata.Payloads;
        AssertSameHash(payloads.Single(p => p.Key == kept.Key).Hash, kept.Hash);
        AssertSameHash(payloads.Single(p => p.Key == added.Key).Hash, added.Hash);
        Assert.That(payloads.Single(p => p.Key == cleared.Key).Hash, Is.Null, "an overwrite without a hash must clear the old one");
    }

    [Test]
    public async Task UpdateWithAWrongStoredHashIsRejected()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var seedMetadata = SampleMetadataData.Create(fileType: 100);
        var file = (await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, seedMetadata)).Content!;

        var payload = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Blake3);
        payload.Content = "not the bytes that were hashed".ToUtf8ByteArray();

        seedMetadata.VersionTag = file.NewVersionTag;
        var update = await owner.Drives.Writer.UpdateFileByFileId(file.DriveId, file.FileId,
            UpdateInstructions(payload.ToPayloadDescriptor(PayloadUpdateOperationType.AppendOrOverwrite)), seedMetadata, [payload]);

        AssertRejected(update, OdinClientErrorCode.PayloadHashMismatch);
        Assert.That((await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!.FileMetadata.Payloads, Is.Empty);
        AssertNoStagedFiles(owner, spec.TargetDrive);
    }

    //
    // Add payload (V1 uploadpayload)
    //

    [Test]
    public async Task AddPayloadVerifiesTheHash()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var drive = owner.V1.Drive;

        var file = (await drive.UploadNewMetadata(spec.TargetDrive, SampleMetadataData.Create(fileType: 100))).Content!;

        var bad = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Sha256);
        bad.Content = "not the bytes that were hashed".ToUtf8ByteArray();
        var rejected = await drive.UploadPayloads(file.File, file.NewVersionTag,
            new UploadManifest { PayloadDescriptors = new[] { bad }.ToPayloadDescriptorList().ToList() }, [bad]);
        AssertRejected(rejected, OdinClientErrorCode.PayloadHashMismatch);
        AssertNoStagedFiles(owner, spec.TargetDrive);

        var good = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Blake3);
        var accepted = await drive.UploadPayloads(file.File, file.NewVersionTag,
            new UploadManifest { PayloadDescriptors = new[] { good }.ToPayloadDescriptorList().ToList() }, [good]);
        Assert.That(accepted.IsSuccessStatusCode, Is.True, $"actual {accepted.StatusCode}");

        var header = (await drive.GetFileHeader(file.File)).Content!;
        AssertSameHash(header.FileMetadata.Payloads.Single(p => p.Key == good.Key).Hash, good.Hash);
    }

    [Test]
    public async Task AddPayloadCleansUpAnEmptyPayloadWhoseHashDoesNotMatch()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var drive = owner.V1.Drive;

        var file = (await drive.UploadNewMetadata(spec.TargetDrive, SampleMetadataData.Create(fileType: 100))).Content!;

        // An empty payload is never added to the package, so its staged file needs its own cleanup on a mismatch
        var empty = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Sha256);
        empty.Content = [];
        var rejected = await drive.UploadPayloads(file.File, file.NewVersionTag,
            new UploadManifest { PayloadDescriptors = new[] { empty }.ToPayloadDescriptorList().ToList() }, [empty]);

        AssertRejected(rejected, OdinClientErrorCode.PayloadHashMismatch);
        AssertNoStagedFiles(owner, spec.TargetDrive);
    }

    //
    // Remote payloads: no bytes pass through, so the hash is optional and only its shape is checked
    //

    [Test]
    public async Task AHashOnARemotePayloadIsValidatedButNotRequired()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        await owner.Admin.SetRequirePayloadHashes(spec.TargetDrive, true);

        var malformed = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Blake3);
        malformed.Hash.StoredHash = new byte[4];
        AssertRejected(await UploadRemote(owner, spec.TargetDrive, malformed), OdinClientErrorCode.InvalidPayloadHash);

        var wellFormed = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Blake3);
        var stored = await UploadRemote(owner, spec.TargetDrive, wellFormed);
        Assert.That(stored.IsSuccessStatusCode, Is.True, $"actual {stored.StatusCode}: {stored.Error?.Content}");
        var header = (await owner.V1.Drive.GetFileHeader(stored.Content!.File)).Content!;
        AssertSameHash(header.FileMetadata.Payloads.Single(p => p.Key == wellFormed.Key).Hash, wellFormed.Hash);

        // requirePayloadHashes applies to bytes written here, which a remote payload never has
        var unhashed = await UploadRemote(owner, spec.TargetDrive, SamplePayloadDefinitions.GetPayloadDefinition2());
        Assert.That(unhashed.IsSuccessStatusCode, Is.True, $"actual {unhashed.StatusCode}: {unhashed.Error?.Content}");
    }

    private static Task<Refit.ApiResponse<UploadResult>> UploadRemote(OwnerSession owner, TargetDrive drive, TestPayloadDefinition payload)
    {
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.DataSource = new DataSource { Identity = new OdinId(Identities.Frodo), DriveId = Guid.NewGuid(), PayloadsAreRemote = true };
        return owner.V1.Drive.UploadNewFile(drive, metadata,
            new UploadManifest { PayloadDescriptors = new[] { payload }.ToPayloadDescriptorList().ToList() }, payloads: []);
    }

    //
    // Binding rule: an encrypted content hash is tied to the payload IV
    //

    [Test]
    public async Task AHashedEncryptedOverwriteMustUseANewPayloadIv()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var drive = owner.V1.Drive;

        var keyHeader = KeyHeader.NewRandom16();
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AppData.Content = "some content";
        var seedPayload = SamplePayloadDefinitions.GetPayloadDefinition1();
        seedPayload.Iv = ByteArrayUtil.GetRndByteArray(16);
        var (seed, _, _, _) = await drive.UploadNewEncryptedFile(spec.TargetDrive, keyHeader, metadata,
            new UploadManifest { PayloadDescriptors = [seedPayload.ToPayloadDescriptor()] }, [seedPayload]);
        Assert.That(seed.IsSuccessStatusCode, Is.True, $"actual {seed.StatusCode}");

        var overwrite = SamplePayloadDefinitions.GetPayloadDefinition1();
        overwrite.Content = "new content for the same key".ToUtf8ByteArray();
        overwrite.Iv = (byte[])seedPayload.Iv.Clone();
        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16); // the header IV must rotate on every update anyway
        overwrite.Hash = V1EncryptedHash(overwrite, keyHeader);
        metadata.VersionTag = seed.Content!.NewVersionTag;

        var (reused, _, _, _) = await drive.UpdateEncryptedFile(OverwriteInstructions(seed.Content.File, overwrite), metadata,
            [overwrite], keyHeader);
        AssertRejected(reused, OdinClientErrorCode.InvalidPayloadHash);

        overwrite.Iv = ByteArrayUtil.GetRndByteArray(16);
        overwrite.Hash = V1EncryptedHash(overwrite, keyHeader);
        metadata.AppData.Content = "some content";
        var (rotated, _, _, _) = await drive.UpdateEncryptedFile(OverwriteInstructions(seed.Content.File, overwrite), metadata,
            [overwrite], keyHeader);
        Assert.That(rotated.IsSuccessStatusCode, Is.True, $"actual {rotated.StatusCode}: {rotated.Error?.Content}");

        var header = (await drive.GetFileHeader(seed.Content.File)).Content!;
        AssertSameHash(header.FileMetadata.Payloads.Single(p => p.Key == overwrite.Key).Hash, overwrite.Hash);
    }

    private static FileUpdateInstructionSet OverwriteInstructions(ExternalFileIdentifier file, TestPayloadDefinition payload) => new()
    {
        Locale = UpdateLocale.Local,
        TransferIv = ByteArrayUtil.GetRndByteArray(16),
        File = file.ToFileIdentifier(),
        Manifest = new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor(PayloadUpdateOperationType.AppendOrOverwrite)] }
    };

    /// <summary>
    /// The V1 test client encrypts payloads with the key header's own IV (not the payload IV it declares), so the
    /// stored bytes are hashed the same way; the content hash uses the declared payload IV, as a real client would.
    /// </summary>
    private static PayloadHash V1EncryptedHash(TestPayloadDefinition payload, KeyHeader keyHeader) => new()
    {
        Algorithm = ContentHashAlgorithm.Blake3,
        StoredHash = IncrementalContentHash.Compute(ContentHashAlgorithm.Blake3, keyHeader.EncryptDataAes(payload.Content)),
        ContentHash = AesCbc.Encrypt(IncrementalContentHash.Compute(ContentHashAlgorithm.Blake3, payload.Content),
            keyHeader.AesKey.GetKey(), PayloadHash.ContentHashIv(payload.Iv))
    };

    //
    // Drive setting
    //

    [Test]
    public async Task ADriveThatRequiresHashesRejectsPayloadsWithoutThem()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        Assert.That((await owner.Admin.GetDrive(spec.TargetDrive)).RequirePayloadHashes, Is.False);
        await owner.Admin.SetRequirePayloadHashes(spec.TargetDrive, true);
        Assert.That((await owner.Admin.GetDrive(spec.TargetDrive)).RequirePayloadHashes, Is.True);

        await AssertCreateRejected(owner, spec.TargetDrive, SamplePayloadDefinitions.GetPayloadDefinition1(),
            OdinClientErrorCode.PayloadHashRequired);

        var hashed = WithUnencryptedHash(SamplePayloadDefinitions.GetPayloadDefinition1(), ContentHashAlgorithm.Blake3);
        await CreateUnencrypted(owner, spec.TargetDrive, hashed);

        // Files without payloads are unaffected
        var metadataOnly = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, SampleMetadataData.Create(fileType: 100));
        Assert.That(metadataOnly.IsSuccessStatusCode, Is.True, $"actual {metadataOnly.StatusCode}");

        await owner.Admin.SetRequirePayloadHashes(spec.TargetDrive, false);
        await CreateUnencrypted(owner, spec.TargetDrive, SamplePayloadDefinitions.GetPayloadDefinition2());
    }

    //
    // Helpers
    //

    private static TestPayloadDefinition WithUnencryptedHash(TestPayloadDefinition payload, ContentHashAlgorithm algorithm)
    {
        var hash = IncrementalContentHash.Compute(algorithm, payload.Content);
        payload.Hash = new PayloadHash { Algorithm = algorithm, StoredHash = hash, ContentHash = (byte[])hash.Clone() };
        return payload;
    }

    /// <summary>
    /// What a client computes: storedHash over the ciphertext it sends, contentHash over the plaintext encrypted
    /// with the file key under the derived IV
    /// </summary>
    private static PayloadHash EncryptedHash(TestPayloadDefinition payload, KeyHeader fileKeyHeader, ContentHashAlgorithm algorithm)
    {
        var payloadKeyHeader = new KeyHeader { Iv = payload.Iv, AesKey = new SensitiveByteArray(fileKeyHeader.AesKey.GetKey()) };
        var ciphertext = payloadKeyHeader.EncryptDataAes(payload.Content);

        var contentHash = IncrementalContentHash.Compute(algorithm, payload.Content);
        return new PayloadHash
        {
            Algorithm = algorithm,
            StoredHash = IncrementalContentHash.Compute(algorithm, ciphertext),
            ContentHash = AesCbc.Encrypt(contentHash, fileKeyHeader.AesKey.GetKey(), PayloadHash.ContentHashIv(payload.Iv))
        };
    }

    private static async Task<CreateFileResult> CreateUnencrypted(OwnerSession owner, TargetDrive drive, TestPayloadDefinition payload)
    {
        var response = await owner.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, SampleMetadataData.Create(fileType: 100),
            new UploadManifest { PayloadDescriptors = new[] { payload }.ToPayloadDescriptorList().ToList() }, [payload]);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"actual {response.StatusCode}: {response.Error?.Content}");
        return response.Content!;
    }

    private async Task AssertCreateRejected(OwnerSession owner, TargetDrive drive, TestPayloadDefinition payload,
        OdinClientErrorCode expected)
    {
        var response = await owner.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, SampleMetadataData.Create(fileType: 100),
            new UploadManifest { PayloadDescriptors = new[] { payload }.ToPayloadDescriptorList().ToList() }, [payload]);
        AssertRejected(response, expected);

        var files = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1(),
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10 }
        });
        Assert.That(files.Content!.SearchResults.Where(f => f.FileMetadata.Payloads.Any(p => p.Key == payload.Key)), Is.Empty,
            "a rejected upload must not create a file");
        AssertNoStagedFiles(owner, drive);
    }

    private static void AssertRejected(Refit.IApiResponse response, OdinClientErrorCode expected)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"actual {response.StatusCode}");
        Assert.That(TestUtils.ParseProblemDetails(response.Error!), Is.EqualTo(expected), response.Error?.Content);
    }

    private void AssertNoStagedFiles(OwnerSession owner, TargetDrive drive)
    {
        var paths = Host.GetTenantScope(owner.Identity.DomainName).Resolve<TenantContext>().TenantPathManager;
        var uploadDirectory = paths.GetDriveUploadPath(drive.Alias);
        var staged = Directory.Exists(uploadDirectory) ? Directory.GetFiles(uploadDirectory, "*", SearchOption.AllDirectories) : [];
        Assert.That(staged, Is.Empty, "the rejected payload must not be left in the upload folder");
    }

    private static async Task<PayloadDescriptor> StoredDescriptor(OwnerSession owner, CreateFileResult file, string key)
    {
        var header = (await owner.Drives.Reader.GetFileHeaderAsync(file.DriveId, file.FileId)).Content!;
        return header.FileMetadata.Payloads.Single(p => p.Key == key);
    }

    private static void AssertSameHash(PayloadHash actual, PayloadHash expected)
    {
        Assert.That(actual, Is.Not.Null, "the stored descriptor has no hash");
        Assert.That(actual.Algorithm, Is.EqualTo(expected.Algorithm));
        Assert.That(actual.StoredHash, Is.EqualTo(expected.StoredHash));
        Assert.That(actual.ContentHash, Is.EqualTo(expected.ContentHash));
    }

    private static FileUpdateInstructionSetV2 UpdateInstructions(params UploadManifestPayloadDescriptor[] payloads) => new()
    {
        Locale = UpdateLocale.Local,
        TransferIv = ByteArrayUtil.GetRndByteArray(16),
        Recipients = null,
        Manifest = new UploadManifest { PayloadDescriptors = payloads.ToList() }
    };
}
