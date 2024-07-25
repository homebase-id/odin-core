using System;
using Odin.Core.Exceptions;
using Odin.Services.Drives;

namespace Odin.Services.Base;

public class FileIdentifier
{
    public Guid FileId { get; set; }

    /// <summary>
    /// The drive to access
    /// </summary>
    public TargetDrive Drive { get; set; }

    public FileIdentifierType Type { get; set; }

    //TODO: consider this for apiv2
    // public FileSystemType FileSystemType { get; set; }

    public void AssertIsValid()
    {
        if (this.FileId == Guid.Empty ||
            !this.Drive.IsValid() ||
            this.Type == FileIdentifierType.NotSet)
        {
            throw new OdinClientException("The file identifier is invalid");
        }
    }

    public void AssertIsValid(FileIdentifierType expectedType)
    {
       this.AssertIsValid();
        AssertIsType(expectedType);
    }
    
    public void AssertIsType(FileIdentifierType expectedType)
    {
        if (this.Type != expectedType)
        {
            throw new OdinClientException("The file identifier type is invalid");
        }
    }


    public bool HasValue()
    {
        return FileId != Guid.NewGuid() && Drive.IsValid();
    }

    public static bool operator ==(FileIdentifier d1, FileIdentifier d2)
    {
        if (ReferenceEquals(d1, d2))
        {
            return true;
        }

        return d1?.FileId == d2?.FileId && d1.Drive == d2.Drive;
    }

    public static bool operator !=(FileIdentifier d1, FileIdentifier d2)
    {
        return !(d1 == d2);
    }

    public bool Equals(FileIdentifier other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Equals(Drive, other.Drive) && FileId.Equals(other.FileId);
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
        return HashCode.Combine(Drive, FileId);
    }
}

public enum FileIdentifierType
{
    NotSet = 0,
    File = 1,
    GlobalTransitId = 2,
    UniqueId = 3
}