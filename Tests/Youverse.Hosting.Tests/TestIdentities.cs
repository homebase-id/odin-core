using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Hosting.Tests
{
    public class TestIdentity
    {
        public DotYouIdentity DotYouId { get; set; }
        public ContactRequestData ContactData { get; set; }
    }
    
    public static class TestIdentities
    {
        public static readonly TestIdentity Frodo = new TestIdentity()
        {
            DotYouId = (DotYouIdentity) "frodo.digital",
            ContactData = new ContactRequestData()
            {
                GivenName = "frodo",
                Surname = "baggins",
                Image = new ImageDataContent()
                {
                    PixelHeight = 400,
                    PixelWidth = 400,
                    ContentType = "image/jpeg",
                    Content = TestMedia.ThumbnailBytes400
                }
            }
        };
        
        public static readonly TestIdentity Samwise = new TestIdentity()
        {
            DotYouId = (DotYouIdentity) "samwise.digital",
            ContactData = new ContactRequestData()
            {
                GivenName = "Samwise",
                Surname = "Gamgee",
                Image = new ImageDataContent()
                {
                    PixelHeight = 300,
                    PixelWidth = 300,
                    ContentType = "image/jpeg",
                    Content = TestMedia.ThumbnailBytes300
                }
            }
        };

        public static Dictionary<string, TestIdentity> All = new Dictionary<string, TestIdentity>()
        {
            { Frodo.DotYouId, Frodo },
            { Samwise.DotYouId, Samwise }
        };
    }
}