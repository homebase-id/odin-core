using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.FileSystem.Standard;

namespace Odin.Services.Base
{
    public class FileSystemResolver
    {
        private readonly StandardFileSystem _standardFileSystem;
        private readonly CommentFileSystem _commentFileSystem;

        /// <summary/> 
        public FileSystemResolver(StandardFileSystem standardFileSystem, CommentFileSystem commentFileSystem)
        {
            _standardFileSystem = standardFileSystem;
            _commentFileSystem = commentFileSystem;
        }

        /// <summary />
        public IDriveFileSystem ResolveFileSystem(FileSystemType fileSystemType)
        {
            if (fileSystemType == FileSystemType.Standard)
            {
                return _standardFileSystem;
            }

            if (fileSystemType == FileSystemType.Comment)
            {
                return _commentFileSystem;
            }

            throw new OdinClientException("Invalid file system type or could not parse instruction set",
                OdinClientErrorCode.InvalidFileSystemType);
        }

        /// <summary>
        /// Gets the file system for the specified file
        /// </summary>
        public async Task<IDriveFileSystem> ResolveFileSystem(InternalDriveFileId file)
        {
            //TODO: this sucks and is wierd.   i don't know at this point if the target file is 
            // comment or standard; so i have to get a IDriveFileSystem instance and look up
            // the type, then get a new IDriveFileSystem

            var fs = this.ResolveFileSystem(FileSystemType.Standard);
            var targetFsType = await fs.Storage.ResolveFileSystemType(file);

            if (targetFsType != FileSystemType.Standard)
            {
                return this.ResolveFileSystem(targetFsType);
            }

            return fs;
        }

        public async Task<(IDriveFileSystem fileSystem, InternalDriveFileId? fileId)> ResolveFileSystem(GlobalTransitIdFileIdentifier globalTransitFileId,
            OdinContext context,
            bool tryCommentDrive = true)
        {
            //TODO: this sucks and is wierd.   i don't know at this point if the target file is 
            // comment or standard; so i have to get a IDriveFileSystem instance and look up
            // the type, then get a new IDriveFileSystem

            var fs = this.ResolveFileSystem(FileSystemType.Standard);
            var file = await fs.Query.ResolveFileId(globalTransitFileId, context);

            if (null == file && tryCommentDrive)
            {
                //try by comment
                fs = this.ResolveFileSystem(FileSystemType.Comment);
                file = await fs.Query.ResolveFileId(globalTransitFileId, context);
            }

            if (null == file)
            {
                return (null, null);
            }

            return (await this.ResolveFileSystem(file.Value), file.Value);
        }
    }
}