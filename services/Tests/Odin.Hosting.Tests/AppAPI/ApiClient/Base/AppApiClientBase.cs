using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.AppAPI.Transit;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Base
{
    public abstract class AppApiClientBase
    {
        private readonly OwnerApiTestUtils _ownerApi;

        public AppApiClientBase(OwnerApiTestUtils ownerApi)
        {
            _ownerApi = ownerApi;
        }
        
        protected HttpClient CreateAppApiHttpClient(OdinId identity, ClientAuthenticationToken token, byte[] sharedSecret, FileSystemType fileSystemType)
        {
            var client = WebScaffold.CreateHttpClient<AppApiClientBase>();

            //
            // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
            // DO NOT do this in production code!
            //
            {
                var cookieValue = $"{YouAuthConstants.AppCookieName}={token}";
                client.DefaultRequestHeaders.Add("Cookie", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(sharedSecret));
            }
            
            client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
            client.Timeout = TimeSpan.FromMinutes(15);
            
            client.BaseAddress = new Uri($"https://{identity}");
            return client;
            
        }

        protected HttpClient CreateAppApiHttpClient(TestAppContext appTestContext, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return CreateAppApiHttpClient(appTestContext.Identity, appTestContext.ClientAuthenticationToken, appTestContext.SharedSecret, fileSystemType);
        }

        protected HttpClient CreateAppApiHttpClient(AppClientToken token, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return CreateAppApiHttpClient(token.OdinId, token.ClientAuthToken, token.SharedSecret, fileSystemType);
        }

    }
}