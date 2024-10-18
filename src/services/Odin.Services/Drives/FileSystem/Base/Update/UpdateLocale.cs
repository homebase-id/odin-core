namespace Odin.Services.Drives.FileSystem.Base.Update;

public enum UpdateLocale
{
    /// <summary>
    /// The update will take place on the local identity.  The FileId must be set on the FileIdentifier
    /// </summary>
    Local = 1,
    
    /// <summary>
    /// The update will take place on the Recipient identity.  This will use the transient
    /// temp drive and ignore any local file that matches the identity.  The GlobalTransitId must be set on the FileIdentifier
    /// </summary>
    Peer = 2
}