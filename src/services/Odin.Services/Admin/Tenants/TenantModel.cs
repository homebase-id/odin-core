using System;
using System.Collections.Generic;

namespace Odin.Services.Admin.Tenants;
#nullable enable

public class TenantModel
{
    public string Domain { get; set; } = "";
    public string Id { get; set; } = "";
    public string RegistrationPath { get; set; } = "";
    public long RegistrationSize { get; set; } = 0;
    public bool Enabled { get; set; }

    public List<PayloadShard>? PayloadShards { get; set; } = null;
    public long? PayloadSize { get; set; } = null;

    public class PayloadShard
    {
        public string Name { get; set; } = "";
        public long Size { get; set; } = 0;
        public string Path { get; set; } = "";
    }
}