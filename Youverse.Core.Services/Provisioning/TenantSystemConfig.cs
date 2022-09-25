namespace Youverse.Core.Services.Provisioning;

public class TenantSystemConfig
{
    public static readonly GuidId ConfigKey = GuidId.FromString("tenant_config");

    /// <summary/>
    public bool AnonymousVisitorsCanViewConnections { get; set; }

    /// <summary/>
    public bool AuthenticatedIdentitiesCanViewConnections { get; set; }

    /// <summary/>
    public bool AllConnectedIdentitiesCanViewConnections { get; set; }
}