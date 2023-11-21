using System.Net.Http;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;

namespace Odin.Hosting.Tests._Universal;

public interface IApiClientFactory
{
    HttpClient CreateHttpClient(OdinId identity, out SensitiveByteArray sharedSecret, FileSystemType fileSystemType = FileSystemType.Standard);
}