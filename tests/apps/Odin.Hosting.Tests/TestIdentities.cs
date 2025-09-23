using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Odin.Core.Identity;
using Odin.Services.Membership.Connections.Requests;
namespace Odin.Hosting.Tests
{
    [DebuggerDisplay("{OdinId.DomainName}")]
    public class TestIdentity
    {
        public OdinId OdinId { get; set; }
        public ContactRequestData ContactData { get; set; }
    }
    public static class TestIdentities
    {
        public static List<string> ToStringList(this IEnumerable<TestIdentity> list)
        {
            return list.Select(d => (string)d.OdinId).ToList();
        }
        //Note: this is not used as a test identity but rather tested against (i.e. auto-follow)
        public static readonly TestIdentity HomebaseId = new TestIdentity()
        {
            OdinId = (OdinId)"id.homebase.id",
            ContactData = new ContactRequestData()
        };
        public static readonly TestIdentity Collab = new TestIdentity()
        {
            OdinId = (OdinId)"collab.dotyou.cloud",
            ContactData = new ContactRequestData()
            {
                Name = "Collaboration Identity",
                ImageId = new Guid()
            }
        };
        public static readonly TestIdentity TomBombadil = new TestIdentity()
        {
            OdinId = (OdinId)"tom.dotyou.cloud",
            ContactData = new ContactRequestData()
            {
                Name = "Tom Bombadil",
                ImageId = new Guid()
            }
        };
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
        public static readonly TestIdentity Merry = new TestIdentity()
        {
            OdinId = (OdinId)"merry.dotyou.cloud",
            ContactData = new ContactRequestData()
            {
                Name = "Merry Brandybuck"
            }
        };
        public static readonly TestIdentity Pippin = new TestIdentity()
        {
            OdinId = (OdinId)"pippin.dotyou.cloud",
            ContactData = new ContactRequestData()
            {
                Name = "Pippin Took"
            }
        };
        public static readonly List<TestIdentity> Defaults = new List<TestIdentity>
        {
            Frodo,
            Samwise,
            Merry,
            Pippin,
            TomBombadil,
            Collab
        };
        public static Dictionary<string, TestIdentity> InitializedIdentities { get; private set; }

        public static void SetCurrent(IEnumerable<TestIdentity> identities)
        {
            if (identities == null)
            {
                InitializedIdentities = Defaults.ToDictionary(i => (string)i.OdinId, i => i);
            }
            else
            {
                InitializedIdentities = identities.ToDictionary(i => (string)i.OdinId, i => i);
            }
        }
    }
}