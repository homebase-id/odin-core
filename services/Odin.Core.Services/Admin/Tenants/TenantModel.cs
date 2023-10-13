using System;

namespace Odin.Core.Services.Admin.Tenants;

public class TenantModel
{
    public string Domain { get; set; }
    public Guid Id { get; set; }
}