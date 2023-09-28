namespace Odin.Core.Services.Configuration;

public enum TenantConfigFlagNames
{
    /// <summary/>
    AnonymousVisitorsCanViewWhoIFollow,
    
    /// <summary/>
    AuthenticatedIdentitiesCanViewWhoIFollow,

    /// <summary/>
    ConnectedIdentitiesCanViewWhoIFollow,
    
    /// <summary/>
    AnonymousVisitorsCanViewConnections,

    /// <summary/>
    AuthenticatedIdentitiesCanViewConnections,

    /// <summary/>
    ConnectedIdentitiesCanViewConnections,
    
    AllAuthenticatedIdentitiesCanReactOnPublicDrives,
    AllAuthenticatedIdentitiesCanCommentOnPublicDrives,
    AllConnectedIdentitiesCanReactOnPublicDrives,
    AllConnectedIdentitiesCanCommentOnPublicDrives
}