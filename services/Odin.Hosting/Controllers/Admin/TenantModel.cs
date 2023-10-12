using System;

namespace Odin.Hosting.Controllers.Admin;

public class TenantModel
{
    public string Domain { get; set; }
    public Guid Id { get; set; }
}