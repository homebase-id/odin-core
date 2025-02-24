using NUnit.Framework;

namespace Odin.Core.Tests
{
    [Ignore("Need to update IdentityCertificate codebase")]
    public class AttributeTests
    {
        // private static IdentityCertificate _id;
        //
        // static OdinId frodo = (OdinId)"frodo.dotyou.cloud";
        // static OdinId samwise = (OdinId)"sam.dotyou.cloud";
        //
        // //IHost webserver;
        // // IdentityContextRegistry _registry;
        //
        // [OneTimeSetUp]
        // public void OneTimeSetUp()
        // {
        //     // _registry = new IdentityContextRegistry();
        //     // _registry.Initialize();
        //     //_id = _registry.ResolveContext()
        // }
        //
        // [OneTimeTearDown]
        // public void OneTimeTearDown()
        // {
        // }
        //
        //
        // [Test(Description = "Name Attribute Pass Test")]
        // public void AttrNameTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new NameAttribute() 
        //       { Prefix = "Mr.", Personal="Frodo", Additional = "Bilboscos",  Surname = "Baggins" , Suffix = "I", FullName = "Mr. Frodo B. Baggins"});
        //     _id.Attributes.Add(m);
        //     m.Value.Id = Guid.Empty;
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        // [Test(Description = "NickName Attribute Pass Test")]
        // public void AttrNickNameTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new NickNameAttribute() { NickName = "Brobo" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        // [Test(Description = "Profile Picture Attribute Pass Test")]
        // public void AttrProfilePicTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new ProfilePicAttribute() { ProfilePic = "c:\\temp\\yay.png" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        // [Test(Description = "Email Attribute Pass Test")]
        // public void AttrEmailTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new EmailAttribute() { Email = "frodo@baggins.com" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        // [Test(Description = "Identity Attribute Pass Test")]
        // public void AttrIdentityTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new IdentityAttribute() { Identity = "sam.gamgee.com" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        // [Test(Description = "BirthDate Attribute Pass Test")]
        // public void AttrBirthDateTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new BirthdateAttribute() { Birthdate = "1000-01-01" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        // [Test(Description = "Anniversary Attribute Pass Test")]
        // public void AttrAnniversaryTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new AnniversaryAttribute() { Anniversary= "1050-01-01" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        //
        // [Test(Description = "Note Attribute Pass Test")]
        // public void AttrNoteTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new NoteAttribute() { Note= "Remember to ask Bilbo where he found the ring." });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        // [Test(Description = "URL Attribute Pass Test")]
        // public void AttrURLTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new UrlAttribute() { Url = "https://www.imdb.com/list/ls055713151/" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        //
        // [Test(Description = "Date Attribute Pass Test")]
        // public void AttrDateTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new DateAttribute() { Date = "0111-03-05" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        // [Test(Description = "DateTime Attribute Pass Test")]
        // public void AttrDateTimeTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new DateTimeAttribute() { DateTime = "0111-03-05 07:13" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        // [Test(Description = "Picture Attribute Pass Test")]
        // public void AttrPictureTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new PictureAttribute() { Picture = "c:\\temp\\samwise.png" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        // [Test(Description = "Twitter Attribute Pass Test")]
        // public void AttrTwitterTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new TwitterAttribute() { Twitter = "@lotro" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        //
        // [Test(Description = "Facebook Attribute Pass Test")]
        // public void AttrFacebookTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new FaceBookAttribute() { FaceBook = "https://www.facebook.com/lordoftheringstrilogy" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        //
        // [Test(Description = "Google Attribute Pass Test")]
        // public void AttrGoogleTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new GoogleAttribute() { Google= "frodo@gmail.com" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        //
        // [Test(Description = "CreditCard Attribute Pass Test")]
        // public void AttrCreditCardTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new CreditCardAttribute() { Number = "1234 5678 9012 3456", Expiration = "10/24", Cvc = "123" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        //
        // [Test(Description = "Phone Attribute Pass Test")]
        // public void AttrPhoneTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new PhoneAttribute() { CountryCode = "+1", Number = "123 456 7890" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
        //
        //
        // [Test(Description = "Address Attribute Pass Test")]
        // public void AttrAddressTest()
        // {
        //     var m = new IdentityAttribute<BaseAttribute>("", new AddressAttribute()
        //       { DisplayAddress = "Under the Hill, Bagend, the Shire", Street = "Willow Rd.", Extension = "12", Locality = "Under the Hill", Region = "The Shire", POBox = "150", PostCode = "2345", Country = "Middleearth", Coordinates = "12.12,1212.221" });
        //     _id.Attributes.Add(m);
        //     ClassicAssert.AreEqual(_id.Attributes.Count, 1);
        // }
    }
}
