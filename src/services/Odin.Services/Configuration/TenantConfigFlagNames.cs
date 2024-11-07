namespace Odin.Services.Configuration;

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
    
    /// <summary/>
    AuthenticatedIdentitiesCanReactOnAnonymousDrives,

    /// <summary/>
    AuthenticatedIdentitiesCanCommentOnAnonymousDrives,
    
    /// <summary/>
    ConnectedIdentitiesCanReactOnAnonymousDrives,
    
    /// <summary/>
    ConnectedIdentitiesCanCommentOnAnonymousDrives,
    
    /// <summary/>
    DisableAutoAcceptIntroductions
}