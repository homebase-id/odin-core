namespace Odin.Services.Drives.FileSystem.V2.Upload.AddNew
{
    /// <summary>
    /// Defines the options for storage
    /// </summary>
    public class StorageOptionsV2
    {
        /// <summary>
        /// The drive in which to store this file
        /// </summary>
        public TargetDrive Drive { get; init; }
    }
}