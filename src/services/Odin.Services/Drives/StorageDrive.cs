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
    public const string BlockCdnAttributeName = "blockcdn";
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
        Directory.CreateDirectory(GetDriveInboxPath());

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

    public bool AttributeHasFalseValue(string attribute)
    {
        if (null == Attributes)
        {
            return false;
        }

        // if the attribute does not exist, the attribute as a false value
        if (!Attributes.TryGetValue(attribute, out string value))
        {
            return true;
        }

        // if the attribute value cannot be parsed, it is a false value
        if (!bool.TryParse(value, out bool flagValue))
        {
            return true;
        }

        return flagValue == false;
    }

    public bool IsCollaborationDrive()
    {
        return this.AttributeHasTrueValue(BuiltInDriveAttributes.IsCollaborativeChannel);
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

    public Dictionary<string, string> Attributes { get; set; }

    public bool IsArchived { get; set; }
}