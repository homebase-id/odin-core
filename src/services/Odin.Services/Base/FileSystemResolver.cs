using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.FileSystem.Standard;

namespace Odin.Services.Base
{
    public class FileSystemResolver(
        StandardFileSystem standardFileSystem,
        CommentFileSystem commentFileSystem,
        DriveQuery driveQuery)
    {
        /// <summary />
        public IDriveFileSystem ResolveFileSystem(FileSystemType fileSystemType)
        {
            if (fileSystemType == FileSystemType.Standard)
            {
                return standardFileSystem;
            }

            if (fileSystemType == FileSystemType.Comment)
            {
                return commentFileSystem;
            }

            throw new OdinClientException("Invalid file system type or could not parse instruction set",
                OdinClientErrorCode.InvalidFileSystemType);
        }

        /// <summary>
        /// Gets the file system for the specified file
        /// </summary>
        public async Task<IDriveFileSystem> ResolveFileSystem(InternalDriveFileId file, IOdinContext odinContext)
        {
            //TODO: this sucks and is wierd.   i don't know at this point if the target file is 
            // comment or standard; so i have to get a IDriveFileSystem instance and look up
            // the type, then get a new IDriveFileSystem

            if (await standardFileSystem.Storage.FileExists(file, odinContext))
            {
                return standardFileSystem;
            }

            if (await commentFileSystem.Storage.FileExists(file, odinContext))
            {
                return commentFileSystem;
            }

            throw new OdinSystemException($"Could not resolve file system type for file {file}");
        }

        public async Task<(IDriveFileSystem fileSystem, InternalDriveFileId? fileId)> ResolveFileSystem(
            GlobalTransitIdFileIdentifier globalTransitFileId,
            IOdinContext odinContext,
            bool tryCommentDrive = true)
        {
            //TODO: this sucks and is wierd.   i don't know at this point if the target file is 
            // comment or standard; so i have to get a IDriveFileSystem instance and look up
            // the type, then get a new IDriveFileSystem

            var fs = this.ResolveFileSystem(FileSystemType.Standard);
            var file = await fs.Query.ResolveFileId(globalTransitFileId, odinContext);

            if (null == file && tryCommentDrive)
            {
                //try by comment
                fs = this.ResolveFileSystem(FileSystemType.Comment);
                file = await fs.Query.ResolveFileId(globalTransitFileId, odinContext);
                return (fs, file);
            }

            return (fs, file);
        }

        public async Task<IDriveFileSystem> ResolveFileSystem(FileIdentifier fileIdentifier, IOdinContext odinContext)
        {
            fileIdentifier.AssertIsValid();

            var t = fileIdentifier.GetFileIdentifierType();

            if (t == FileIdentifierType.GlobalTransitId)
            {
                var record = await driveQuery.GetByGlobalTransitIdAsync(fileIdentifier.DriveId,
                    fileIdentifier.GlobalTransitId.GetValueOrDefault());
                if (null == record)
                {
                    throw new OdinClientException($"Could not find file with global transit id {fileIdentifier.GlobalTransitId}");
                }

                return this.ResolveFileSystem((FileSystemType)record.fileSystemType);
            }

            if (t == FileIdentifierType.UniqueId)
            {
                var record = await driveQuery.GetByClientUniqueIdAsync(fileIdentifier.DriveId, fileIdentifier.UniqueId.GetValueOrDefault());
                if (null == record)
                {
                    throw new OdinClientException($"Could not find file with uniqueId {fileIdentifier.GlobalTransitId}");
                }

                return this.ResolveFileSystem((FileSystemType)record.fileSystemType);
            }

            if (t == FileIdentifierType.File)
            {
                // old way
                var file = new InternalDriveFileId(fileIdentifier.DriveId, fileIdentifier.FileId.GetValueOrDefault());
                return await this.ResolveFileSystem(file, odinContext);
            }

            return null;
        }
    }
}