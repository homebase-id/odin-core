using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.FileSystem.Standard;

namespace Odin.Services.Base
{
    public class FileSystemResolver(StandardFileSystem standardFileSystem, CommentFileSystem commentFileSystem)
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
        public async Task<IDriveFileSystem> ResolveFileSystem(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            //TODO: this sucks and is wierd.   i don't know at this point if the target file is 
            // comment or standard; so i have to get a IDriveFileSystem instance and look up
            // the type, then get a new IDriveFileSystem
            
            if (await standardFileSystem.Storage.FileExists(file, odinContext, db))
            {
                return standardFileSystem;
            }

            if (await commentFileSystem.Storage.FileExists(file, odinContext, db))
            {
                return commentFileSystem;
            }

            throw new OdinSystemException($"Could not resolve file system type for file {file}");
        }

        public async Task<(IDriveFileSystem fileSystem, InternalDriveFileId? fileId)> ResolveFileSystem(GlobalTransitIdFileIdentifier globalTransitFileId,
            IOdinContext odinContext, IdentityDatabase db,
            bool tryCommentDrive = true)
        {
            //TODO: this sucks and is wierd.   i don't know at this point if the target file is 
            // comment or standard; so i have to get a IDriveFileSystem instance and look up
            // the type, then get a new IDriveFileSystem

            var fs = this.ResolveFileSystem(FileSystemType.Standard);
            var file = await fs.Query.ResolveFileId(globalTransitFileId, odinContext, db);

            if (null == file && tryCommentDrive)
            {
                //try by comment
                fs = this.ResolveFileSystem(FileSystemType.Comment);
                file = await fs.Query.ResolveFileId(globalTransitFileId, odinContext, db);
                return (fs, file);
            }

            return (fs, file);
        }
    }
}