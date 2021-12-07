﻿#nullable enable
namespace Youverse.Core.Services.Tenant
{
    public class Tenant
    {
        public string Name { get; init; }

        public Tenant(string name)
        {
            Name = name;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}