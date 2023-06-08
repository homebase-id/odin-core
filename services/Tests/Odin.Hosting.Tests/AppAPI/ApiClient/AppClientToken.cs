using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Hosting.Tests.AppAPI.ApiClient;

public class AppClientToken
{
    public OdinId OdinId { get; set; }
    public ClientAuthenticationToken ClientAuthToken { get; set; }
    public byte[] SharedSecret { get; set; }
}