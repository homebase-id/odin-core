using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Services.Contacts.Circle.Requests;

namespace Odin.Hosting.Tests
{
    public class TestIdentity
    {
        public OdinId OdinId { get; set; }
        public ContactRequestData ContactData { get; set; }
    }

    public static class TestIdentities
    {
        public static readonly TestIdentity Frodo = new TestIdentity()
        {
            OdinId = (OdinId)"frodo.dotyou.cloud",
            ContactData = new ContactRequestData()
            {
                Name = "frodo baggins",
                ImageId = new Guid()
            }
        };

        public static readonly TestIdentity Samwise = new TestIdentity()
        {
            OdinId = (OdinId)"sam.dotyou.cloud",
            ContactData = new ContactRequestData()
            {
                Name = "Samwise Gamgee",
                ImageId = new Guid()
            }
        };

        public static readonly TestIdentity Merry = new()
        {
            OdinId = (OdinId)"merry.dotyou.cloud",
            ContactData = new ContactRequestData()
            {
                Name = "Merry Brandybuck"
            }
        };

        public static readonly TestIdentity Pippin = new()
        {
            OdinId = (OdinId)"pippin.dotyou.cloud",
            ContactData = new ContactRequestData()
            {
                Name = "Pippin Took"
            }
        };

        public static Dictionary<string, TestIdentity> All = new Dictionary<string, TestIdentity>()
        {
            { Frodo.OdinId, Frodo },
            { Samwise.OdinId, Samwise },
            { Merry.OdinId, Merry },
            { Pippin.OdinId, Pippin }
        };
    }
}
