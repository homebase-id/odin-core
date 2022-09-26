namespace Youverse.Core.Services.Provisioning;

public class TenantSystemConfig
{
    public static readonly GuidId ConfigKey = GuidId.FromString("tenant_config");

    private static TenantSystemConfig _default = new TenantSystemConfig()
    {
        AnonymousVisitorsCanViewConnections = false,
        AuthenticatedIdentitiesCanViewConnections = false,
        AllConnectedIdentitiesCanViewConnections = false
    };
    
    /// <summary/>
    public bool AnonymousVisitorsCanViewConnections { get; set; }

    /// <summary/>
    public bool AuthenticatedIdentitiesCanViewConnections { get; set; }

    /// <summary/>
    public bool AllConnectedIdentitiesCanViewConnections { get; set; }

    public static TenantSystemConfig Default => _default;
}