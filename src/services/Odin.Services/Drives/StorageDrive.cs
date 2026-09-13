using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Odin.Services.Drives;

/// <summary>
/// Information about a drive
/// </summary>
[DebuggerDisplay("{Name} AllowAnon={AllowAnonymousReads} AllowSubs={AllowSubscriptions} ReadOnly={IsReadonly}")]
public sealed class StorageDrive(TenantPathManager tenantPathManager, StorageDriveData data)
{
    internal StorageDriveData Data { get; } = data;

    public Guid Id => Data.Id;
    public string Name => Data.Name;
    public TargetDrive TargetDriveInfo => Data.TargetDriveInfo;
    public Guid TempOriginalDriveId => Data.TempOriginalDriveId;

    public string Metadata
    {
        get => Data.Metadata;
        set => Data.Metadata = value;
    }

    public bool IsReadonly
    {
        get => Data.IsReadonly;
        set => Data.IsReadonly = value;
    }

    public bool AllowSubscriptions
    {
        get => Data.AllowSubscriptions;
        set => Data.AllowSubscriptions = value;
    }

    public bool IsArchived
    {
        get => Data.IsArchived;
        set => Data.IsArchived = value;
    }

    public SymmetricKeyEncryptedAes MasterKeyEncryptedStorageKey => Data.MasterKeyEncryptedStorageKey;

    /// <summary>The drive's write-only keypair; see <see cref="StorageDriveData.WriteOnlyKeyPair"/>.</summary>
    public EccFullKeyData WriteOnlyKeyPair
    {
        get => Data.WriteOnlyKeyPair;
        set => Data.WriteOnlyKeyPair = value;
    }

    public byte[] EncryptedIdIv => Data.EncryptedIdIv;
    public byte[] EncryptedIdValue => Data.EncryptedIdValue;

    public bool AllowAnonymousReads
    {
        get => Data.AllowAnonymousReads;
        set => Data.AllowAnonymousReads = value;
    }


    public Dictionary<string, string> Attributes
    {
        get => Data.Attributes ?? new Dictionary<string, string>();
        set => Data.Attributes = value;
    }

    public bool OwnerOnly => Data.OwnerOnly;

    public Guid? AppId
    {
        get => Data.AppId;
        set => Data.AppId = value;
    }

    public string DriveSlug
    {
        get => Data.DriveSlug;
        set => Data.DriveSlug = value;
    }

    public string DriveTypeSlug
    {
        get => Data.DriveTypeSlug;
        set => Data.DriveTypeSlug = value;
    }

    public string GetDrivePayloadPath()
    {
        return tenantPathManager.GetDrivePayloadPath(Id);
    }

    public string GetDriveUploadPath()
    {
        return tenantPathManager.GetDriveUploadPath(Id);
    }

    public string GetDriveInboxPath()
    {
        return tenantPathManager.GetDriveInboxPath(Id);
    }

    public void CreateDirectories()
    {
        string payloadDirectory = GetDrivePayloadPath();

        // Just for sanity, to see if anything fails
        if (!tenantPathManager.S3PayloadsEnabled && Directory.Exists(payloadDirectory))
        {
            throw new Exception("CreateDirectories() called but drive folder already exists on disk.");
        }

        Directory.CreateDirectory(GetDriveUploadPath());

        // TODO:INBOX The per-drive inbox folder is intentionally NOT created: nothing writes to it anymore
        // (peer payloads stream to long-term, metadata rides the inbox row). The read/cleanup paths that remain
        // for draining legacy items tolerate its absence. Drop GetDriveInboxPath with the rest once the folder is gone.

        if (!tenantPathManager.S3PayloadsEnabled && !string.IsNullOrEmpty(payloadDirectory))
        {
            Directory.CreateDirectory(payloadDirectory);
        }

        /* This code will oddly cause Overwrite_Encrypted_PayloadManyTimes_Concurrently_MultipleThreads TEST to fail
        for (int first = 0; first < 16; first++)
        {
            Directory.CreateDirectory(Path.Combine(payloadDirectory, first.ToString("x")));

            for (int second = 0; second < 16; second++)
            {
                Directory.CreateDirectory(Path.Combine(payloadDirectory, first.ToString("x"), second.ToString("x")));
            }
        }
        */
    }

    public void AssertValidStorageKey(SensitiveByteArray storageKey)
    {
        var decryptedDriveId = AesCbc.Decrypt(this.EncryptedIdValue, storageKey, this.EncryptedIdIv);
        if (!ByteArrayUtil.EquiByteArrayCompare(decryptedDriveId, this.TempOriginalDriveId.ToByteArray()))
        {
            throw new OdinSecurityException("Invalid key storage attempted to encrypt data");
        }
    }

    public bool AttributeHasTrueValue(string attribute)
    {
        if (null == Attributes)
        {
            return false;
        }

        return this.Attributes.TryGetValue(attribute, out string value) &&
               bool.TryParse(value, out bool flagValue) &&
               flagValue;
    }

    public bool IsCollaborationDrive()
    {
        return this.AttributeHasTrueValue(BuiltInDriveAttributes.IsCollaborativeChannel);
    }

    /// <summary>
    /// Whether the CDN may read this drive's payloads.
    ///
    /// The single place anything asks that question - go through this rather than reading the
    /// underlying flag, so the rule can be changed here without touching call sites.
    ///
    /// Note this only gates drives that actually need a grant: an AllowAnonymousReads drive's
    /// payloads are world-readable, so the CDN reaches them whatever this returns.
    /// </summary>
    public bool IsCdnEnabled()
    {
        return Data.AllowCdn;
    }

    /// <summary>
    /// Sets CDN eligibility on this in-memory drive. Persisting it is
    /// <see cref="Odin.Services.Drives.Management.DriveManager.SetDriveAllowCdnAsync"/>.
    /// </summary>
    public void SetCdnEnabled(bool value)
    {
        Data.AllowCdn = value;
    }
}

// This guy needs to be serializable
public sealed class StorageDriveData
{
    public Guid Id { get; init; }

    public Guid TempOriginalDriveId { get; init; }

    public string Name { get; init; }

    /// <summary>
    /// Data specified by the client to further help with usage of this drive (i.e. a json string indicating things like description, etc.)
    /// </summary>
    public string Metadata { get; set; }

    public bool OwnerOnly { get; init; }

    /// <summary>
    /// Specifies a public identifier for accessing this drive.  This stops us from sharing the Id outside of this system.
    /// </summary>
    public TargetDrive TargetDriveInfo { get; init; }

    /// <summary>
    /// Specifies the drive can only be written to by the owner while in the OwnerAuth context
    /// </summary>
    public bool IsReadonly { get; set; }

    /// <summary>
    /// The encryption key used to encrypt the <see cref="ServerFileHeader.EncryptedKeyHeader"/>
    /// </summary>
    public SymmetricKeyEncryptedAes MasterKeyEncryptedStorageKey { get; set; }

    public byte[] EncryptedIdIv { get; init; }

    public byte[] EncryptedIdValue { get; init; }

    /// <summary>
    /// Specifies if anonymous callers can read this drive.
    /// </summary>
    public bool AllowAnonymousReads { get; set; }

    /// <summary>
    /// Indicates if the drive allows data subscriptions to be configured.  It is an error
    /// for a drive to be marked OwnerOnly == true and AllowSubscriptions === true
    /// </summary>
    public bool AllowSubscriptions { get; set; }

    /// <summary>
    /// Specifies if the CDN may read this drive's payloads. Opt-in; see <see cref="StorageDriveDetails"/>.
    /// </summary>
    public bool AllowCdn { get; set; }

    /// <summary>
    /// The app that owns this drive; null means an owner drive.
    /// </summary>
    /// <remarks>
    /// <see cref="AppId"/>, <see cref="DriveSlug"/> and <see cref="DriveTypeSlug"/> are columns on the
    /// Drives table, not part of <see cref="StorageDriveDetails"/>.  They are deliberately kept out of
    /// detailsJson: <c>UNIQUE(identityId, AppId, DriveSlug)</c> constrains the columns, and a second copy
    /// in the blob could disagree with what the constraint is enforcing.
    /// <para>
    /// All three ship dormant.  Nothing derives them yet, so every drive carries null until the
    /// addressing work assigns them; see <c>docs/drive-addressing.md</c>.
    /// </para>
    /// </remarks>
    public Guid? AppId { get; set; }

    /// <summary>
    /// The drive's portable name -- the segment a remote caller uses to address it
    /// (<c>/apps/{appSlug}/drives/{driveSlug}</c>).  Null when <see cref="AppId"/> is null.
    /// </summary>
    public string DriveSlug { get; set; }

    /// <summary>
    /// Readable form of the drive's type, e.g. <c>channel</c>.  A category to filter on, not an address.
    /// </summary>
    public string DriveTypeSlug { get; set; }

    /// <summary>
    /// The drive's write-only keypair -- an ECC-384 <c>EccFullKeyData</c> holding the public half in
    /// clear and the private half escrowed under this drive's own storage key.  Anyone with the public
    /// half can seal a deposit to the drive; only a holder of the storage key can unseal it, so
    /// deposit-collection custody is exactly existing read access (docs/drive-addressing.md).
    /// </summary>
    /// <remarks>
    /// A column, not part of <see cref="StorageDriveDetails"/>, for the same reason the addressing
    /// fields are: it is minted with the storage key in scope and has no business round-tripping
    /// through a blob that other writers rebuild.
    /// <para>
    /// Null on every drive that predates this and on any drive whose creation had no master key in
    /// scope.  The v14 -&gt; v15 migration backfills existing drives.
    /// </para>
    /// </remarks>
    public EccFullKeyData WriteOnlyKeyPair { get; set; }

    public Dictionary<string, string> Attributes { get; set; }

    public bool IsArchived { get; set; }
}