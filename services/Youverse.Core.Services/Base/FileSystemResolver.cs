using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Exceptions;
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

            throw new YouverseClientException("Invalid file system type or could not parse instruction set", YouverseClientErrorCode.InvalidFileSystemType);
        }
    }
}