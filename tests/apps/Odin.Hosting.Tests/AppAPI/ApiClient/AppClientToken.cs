using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Tests.AppAPI.ApiClient;

public class AppClientToken
{
    public OdinId OdinId { get; set; }
    public ClientAuthenticationToken ClientAuthToken { get; set; }
    public byte[] SharedSecret { get; set; }
}