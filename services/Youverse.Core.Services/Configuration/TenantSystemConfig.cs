namespace Youverse.Core.Services.Configuration;

public class TenantSystemConfig
{
    public static readonly GuidId ConfigKey = GuidId.FromString("tenant_config");

    public static TenantSystemConfig Default { get; } = new TenantSystemConfig()
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
    
}