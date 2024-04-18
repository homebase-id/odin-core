using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

        /// <summary />
        public IDriveFileSystem ResolveFileSystem(FileSystemType fileSystemType)
        {
            var ctx = _contextAccessor.HttpContext;

            if (fileSystemType == FileSystemType.Standard)
            {
                return ctx!.RequestServices.GetRequiredService<StandardFileSystem>();
            }

            if (fileSystemType == FileSystemType.Comment)
            {
                return ctx!.RequestServices.GetRequiredService<CommentFileSystem>();
            }

            throw new OdinClientException("Invalid file system type or could not parse instruction set",
                OdinClientErrorCode.InvalidFileSystemType);
        }

        /// <summary>
        /// Gets the file system for the specified file
        /// </summary>
        public async Task<IDriveFileSystem> ResolveFileSystem(InternalDriveFileId file, OdinContext odinContext)
        {
            //TODO: this sucks and is wierd.   i don't know at this point if the target file is 
            // comment or standard; so i have to get a IDriveFileSystem instance and look up
            // the type, then get a new IDriveFileSystem

            var fs = this.ResolveFileSystem(FileSystemType.Standard);
            var targetFsType = await fs.Storage.ResolveFileSystemType(file, odinContext);

            if (targetFsType != FileSystemType.Standard)
            {
                return this.ResolveFileSystem(targetFsType);
            }

            return fs;
        }

        public async Task<(IDriveFileSystem fileSystem, InternalDriveFileId? fileId)> ResolveFileSystem(GlobalTransitIdFileIdentifier globalTransitFileId,
            OdinContext odinContext,
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
            }

            if (null == file)
            {
                return (null, null);
            }

            return (await this.ResolveFileSystem(file.Value, odinContext), file.Value);
        }
    }
}