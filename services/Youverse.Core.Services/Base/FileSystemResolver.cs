using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Comment;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Base
{
    public class FileSystemResolver
    {
        private readonly IHttpContextAccessor _contextAccessor;

        /// <summary/> 
        public FileSystemResolver(IHttpContextAccessor contextAccessor)
        {
            _contextAccessor = contextAccessor;
        }

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

            throw new YouverseClientException("Invalid file system type or could not parse instruction set",
                YouverseClientErrorCode.InvalidFileSystemType);
        }

        /// <summary>
        /// Gets the file system for the specified file
        /// </summary>
        public IDriveFileSystem ResolveFileSystem(InternalDriveFileId file)
        {
            //TODO: this sucks and is wierd.   i don't know at this point if the target file is 
            // comment or standard; so i have to get a IDriveFileSystem instance and look up
            // the type, then get a new IDriveFileSystem

            var fs = this.ResolveFileSystem(FileSystemType.Standard);
            var targetFsType = fs.Storage.ResolveFileSystemType(file).GetAwaiter().GetResult();

            if (targetFsType != FileSystemType.Standard)
            {
                return this.ResolveFileSystem(targetFsType);
            }

            return fs;
        }

        public async Task<(IDriveFileSystem fileSystem, InternalDriveFileId? fileId)> ResolveFileSystem(GlobalTransitIdFileIdentifier globalTransitFileId, bool tryCommentDrive = true)
        {
            //TODO: this sucks and is wierd.   i don't know at this point if the target file is 
            // comment or standard; so i have to get a IDriveFileSystem instance and look up
            // the type, then get a new IDriveFileSystem

            var fs = this.ResolveFileSystem(FileSystemType.Standard);
            var file = await fs.Query.ResolveFileId(globalTransitFileId);
            
            if (null == file && tryCommentDrive)
            {
                //try by comment
                fs = this.ResolveFileSystem(FileSystemType.Comment);
                file = await fs.Query.ResolveFileId(globalTransitFileId);
            }

            if (null == file)
            {
                return (null, null);
            }

            return (this.ResolveFileSystem(file.Value), file.Value);
        }
    }
}