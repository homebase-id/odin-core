using System;
using Odin.Core.Exceptions;
using Odin.Services.Drives;

namespace Odin.Services.Base;

public class FileIdentifier
{
    public Guid? FileId { get; init; }
    public Guid? GlobalTransitId { get; init; }
    public Guid? UniqueId { get; init; }

    /// <summary>
    /// The drive to access
    /// </summary>
    public TargetDrive TargetDrive { get; set; }
    
    public Guid DriveId { get; init; }

    public void AssertIsValid()
    {
        var missingField = !FileIdHasValue &&
                           !GlobalTransitIdHasValue &&
                           !UniqueIdHasValue;

        if (missingField || !this.TargetDrive.IsValid())
        {
            throw new OdinClientException("The file identifier is invalid");
        }

        if (FileIdHasValue && (GlobalTransitIdHasValue || UniqueIdHasValue))
        {
            throw new OdinClientException("The file identifier is invalid; only one field can be set");
        }

        if (GlobalTransitIdHasValue && (FileIdHasValue || UniqueIdHasValue))
        {
            throw new OdinClientException("The file identifier is invalid; only one field can be set");
        }

        if (UniqueIdHasValue && (GlobalTransitIdHasValue || FileIdHasValue))
        {
            throw new OdinClientException("The file identifier is invalid; only one field can be set");
        }
    }

    public void AssertIsValid(FileIdentifierType expectedType)
    {
        this.AssertIsValid();
        AssertIsType(expectedType);
    }

    public FileIdentifierType GetFileIdentifierType()
    {
        this.AssertIsValid();

        if (FileIdHasValue)
        {
            return FileIdentifierType.File;
        }

        if (GlobalTransitIdHasValue)
        {
            return FileIdentifierType.GlobalTransitId;
        }

        if (UniqueIdHasValue)
        {
            return FileIdentifierType.UniqueId;
        }

        return FileIdentifierType.NotSet;
    }

    public static bool operator ==(FileIdentifier d1, FileIdentifier d2)
    {
        throw new NotImplementedException("Need to check across all fields");
        // if (ReferenceEquals(d1, d2))
        // {
        //     return true;
        // }
        //
        // return d1?.FileId == d2?.FileId && d1.TargetDrive == d2.TargetDrive;
    }

    public static bool operator !=(FileIdentifier d1, FileIdentifier d2)
    {
        return !(d1 == d2);
    }

    public bool Equals(FileIdentifier other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Equals(TargetDrive, other.TargetDrive) && FileId.Equals(other.FileId);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((FileIdentifier)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TargetDrive, FileId);
    }

    ///
    private bool FileIdHasValue => this.FileId.GetValueOrDefault() != Guid.Empty;

    private bool GlobalTransitIdHasValue => this.GlobalTransitId.GetValueOrDefault() != Guid.Empty;
    private bool UniqueIdHasValue => this.UniqueId.GetValueOrDefault() != Guid.Empty;

    private void AssertIsType(FileIdentifierType expectedType)
    {
        switch (expectedType)
        {
            case FileIdentifierType.File:
                if (!FileIdHasValue)
                {
                    throw new OdinClientException("The file identifier type is invalid");
                }

                break;
            case FileIdentifierType.GlobalTransitId:
                if (!GlobalTransitIdHasValue)
                {
                    throw new OdinClientException("The file identifier type is invalid");
                }

                break;
            case FileIdentifierType.UniqueId:
                if (!UniqueIdHasValue)
                {
                    throw new OdinClientException("The file identifier type is invalid");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expectedType), expectedType, null);
        }
    }

    public GlobalTransitIdFileIdentifier ToGlobalTransitIdFileIdentifier()
    {
        return new()
        {
            TargetDrive = this.TargetDrive,
            GlobalTransitId = this.GlobalTransitId.GetValueOrDefault()
        };
    }
}

public enum FileIdentifierType
{
    NotSet = 0,
    File = 1,
    GlobalTransitId = 2,
    UniqueId = 3
}