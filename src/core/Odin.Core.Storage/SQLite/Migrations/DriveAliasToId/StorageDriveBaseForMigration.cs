using System;
using System.Collections.Generic;
using Odin.Core.Cryptography.Data;

namespace Odin.Core.Storage.SQLite.Migrations.DriveAliasToId;

public class StorageDriveBaseForMigration
{
    public virtual Guid Id { get; init; }

    public virtual string Name { get; set; }

    /// <summary>
    /// Data specified by the client to further help with usage of this drive (i.e. a json string indicating things like description, etc.)
    /// </summary>
    public virtual string Metadata { get; set; }

    public virtual bool OwnerOnly { get; set; }

    /// <summary>
    /// Specifies a public identifier for accessing this drive.  This stops us from sharing the Id outside of this system.
    /// </summary>
    public virtual TargetDriveForMigration TargetDriveInfo { get; set; }

    /// <summary>
    /// Specifies the drive can only be written to by the owner while in the OwnerAuth context
    /// </summary>
    public virtual bool IsReadonly { get; set; }

    /// <summary>
    /// The encryption key used to encrypt the <see cref="ServerFileHeader.EncryptedKeyHeader"/>
    /// </summary>
    public virtual SymmetricKeyEncryptedAes MasterKeyEncryptedStorageKey { get; set; }

    public virtual byte[] EncryptedIdIv { get; set; }

    public virtual byte[] EncryptedIdValue { get; set; }

    /// <summary>
    /// Specifies if anonymous callers can read this drive.
    /// </summary>
    public virtual bool AllowAnonymousReads { get; set; }

    /// <summary>
    /// Indicates if the drive allows data subscriptions to be configured.  It is an error
    /// for a drive to be marked OwnerOnly == true and AllowSubscriptions === true
    /// </summary>
    public virtual bool AllowSubscriptions { get; set; }

    public virtual Dictionary<string, string> Attributes { get; set; }
}