using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Optimization.Cdn;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.Base.Cdn
{
    public abstract class StaticFileContentPublishControllerBase(StaticFileContentService staticFileContentService, TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        /// <summary>
        /// Creates a static file which contents match the query params.  Accessible to the public
        /// as it will only contain un-encrypted content targeted at Anonymous users
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("publish")]
        public async Task<StaticFilePublishResult> PublishBatch([FromBody] PublishStaticFileRequest request)
        {
            OdinValidationUtils.AssertNotNullOrEmpty(request.Filename, nameof(request.Filename));
            OdinValidationUtils.AssertValidFileName(request.Filename, "The file name is invalid");
            OdinValidationUtils.AssertNotNull(request.Sections, nameof(request.Sections));
            OdinValidationUtils.AssertIsTrue(request.Sections.Count != 0, "At least one section is needed");
            var db = tenantSystemStorage.IdentityDatabase;
            var publishResult = await staticFileContentService.Publish(request.Filename, request.Config, request.Sections, WebOdinContext, db);
            return publishResult;
        }
    }
}