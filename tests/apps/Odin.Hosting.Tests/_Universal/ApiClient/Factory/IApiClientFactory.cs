using System.Net.Http;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;

namespace Odin.Hosting.Tests._Universal.ApiClient.Factory;

public interface IApiClientFactory
{
    public SensitiveByteArray SharedSecret { get; }
    HttpClient CreateHttpClient(OdinId identity, out SensitiveByteArray sharedSecret, FileSystemType fileSystemType = FileSystemType.Standard);
}