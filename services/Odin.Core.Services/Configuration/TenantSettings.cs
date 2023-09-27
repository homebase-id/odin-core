namespace Odin.Core.Services.Configuration;

public class TenantSettings
{
    public static readonly GuidId ConfigKey = GuidId.FromString("tenant_config");

    public static TenantSettings Default { get; } = new TenantSettings()
    {
        AnonymousVisitorsCanViewConnections = false,
        AuthenticatedIdentitiesCanViewConnections = false,
        AllConnectedIdentitiesCanViewConnections = false,
        AnonymousVisitorsCanViewWhoIFollow = false,
        AuthenticatedIdentitiesCanViewWhoIFollow = false,
        AllConnectedIdentitiesCanViewWhoIFollow = false,
        AllAuthenticatedIdentitiesCanCommentOnPublicDrives = false,
        AllAuthenticatedIdentitiesCanReactOnPublicDrives = true,
        AllConnectedIdentitiesCanReactOnPublicDrives = true,
        AllConnectedIdentitiesCanCommentOnPublicDrives = true
    };

    /// <summary/>
    public bool AnonymousVisitorsCanViewWhoIFollow { get; set; }

    public bool AuthenticatedIdentitiesCanViewWhoIFollow { get; set; }

    /// <summary/>
    public bool AllConnectedIdentitiesCanViewWhoIFollow { get; set; }
    
    /// <summary/>
    public bool AnonymousVisitorsCanViewConnections { get; set; }

    /// <summary/>
    public bool AuthenticatedIdentitiesCanViewConnections { get; set; }

    /// <summary/>
    public bool AllConnectedIdentitiesCanViewConnections { get; set; }

    public bool AllAuthenticatedIdentitiesCanReactOnPublicDrives { get; set; }
    
    public bool AllAuthenticatedIdentitiesCanCommentOnPublicDrives { get; set; }
    
    public bool AllConnectedIdentitiesCanReactOnPublicDrives { get; set; }
    
    public bool AllConnectedIdentitiesCanCommentOnPublicDrives { get; set; }
}