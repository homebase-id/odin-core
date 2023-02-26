using System;
using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Requests;

namespace Youverse.Hosting.Tests
{
    public class TestIdentity
    {
        public OdinId DotYouId { get; set; }
        public ContactRequestData ContactData { get; set; }
    }

    public static class TestIdentities
    {
        public static readonly TestIdentity Frodo = new TestIdentity()
        {
            DotYouId = (OdinId)"frodo.digital",
            ContactData = new ContactRequestData()
            {
                Name = "frodo baggins",
                ImageId = new Guid()
            }
        };

        public static readonly TestIdentity Samwise = new TestIdentity()
        {
            DotYouId = (OdinId)"samwise.digital",
            ContactData = new ContactRequestData()
            {
                Name = "Samwise Gamgee",
                ImageId = new Guid()
            }
        };

        public static readonly TestIdentity Merry = new()
        {
            DotYouId = (OdinId)"merry.youfoundation.id",
            ContactData = new ContactRequestData()
            {
                Name = "Merry Brandybuck"
            }
        };

        public static readonly TestIdentity Pippin = new()
        {
            DotYouId = (OdinId)"pippin.youfoundation.id",
            ContactData = new ContactRequestData()
            {
                Name = "Pippin Took"
            }
        };

        public static Dictionary<string, TestIdentity> All = new Dictionary<string, TestIdentity>()
        {
            { Frodo.DotYouId, Frodo },
            { Samwise.DotYouId, Samwise },
            { Merry.DotYouId, Merry },
            { Pippin.DotYouId, Pippin }
        };
    }
}
