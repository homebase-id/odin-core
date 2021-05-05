using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public enum AttributeTypes
    {
        None = 0,
        Name = 10,
        NickName = 11,
        ProfilePic = 12,
        Address = 20,
        Phone = 21,
        Email = 22,
        Identity = 23,
        Birthdate = 24,
        Anniversary = 25,

        CreditCard = 30,

        Note = 40,
        URL = 50,
        Date = 60,
        DateTime = 70,
        Picture = 80, // ? File? Multi-media? (video, audio, ...).
                      // GPS corrdinate

        // Company name 100 including title and more?

        Twitter = 666,
        FaceBook = 667,
        Google = 668
    }

    // XXX I'd like the Id and AttrType to be readonly 
    public abstract class BaseAttribute
    {
        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonProperty("attributeType")]
        public abstract int AttributeType { get; set; }

    }

    public class NameAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Name; } set { } }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(this.FullName) || string.IsNullOrWhiteSpace(this.FullName))
                return this.Prefix + " " + this.Personal + " " + this.Surname + " " + this.Additional + " " +
                       this.Suffix;
            else
                return this.FullName;
        }

        [JsonProperty("prefix")]
        public string Prefix { get; set; }

        [JsonProperty("personal")]
        public string Personal { get; set; }

        [JsonProperty("additional")]
        public string Additional { get; set; }

        [JsonProperty("surname")]
        public string Surname { get; set; }

        [JsonProperty("suffix")]
        public string Suffix { get; set; }

        [JsonProperty("fullName")]
        public string FullName { get; set; }
    }

    public class NickNameAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.NickName; } set { } }

        public override string ToString()
        {
            return this.NickName;
        }

        [JsonProperty("nickName")]
        public string NickName { get; set; }
    }

    public class ProfilePicAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.ProfilePic; } set { } }

        public override string ToString()
        {
            return this.ProfilePic;
        }

        [JsonProperty("profilePic")]
        public string ProfilePic { get; set; }
    }


    public class EmailAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Email; } set { } }

        public override string ToString()
        {
            return this.Email;
        }

        [JsonProperty("email")]
        public string Email { get; set; }
    }

    public class IdentityAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Identity; } set { } }

        public override string ToString()
        {
            return this.Identity;
        }

        [JsonProperty("identity")]
        public string Identity { get; set; }
    }

    public class BirthdateAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Birthdate; } set { } }

        public override string ToString()
        {
            return this.Birthdate;
        }

        [JsonProperty("birthdate")]
        public string Birthdate { get; set; }
    }

    public class AnniversaryAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Anniversary; } set { } }

        public override string ToString()
        {
            return this.Anniversary;
        }

        [JsonProperty("anniversary")]
        public string Anniversary { get; set; }
    }

    public class NoteAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Note; } set { } }

        public override string ToString()
        {
            return this.Note;
        }

        [JsonProperty("note")]
        public string Note { get; set; }
    }

    public class UrlAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.URL; } set { } }

        public override string ToString()
        {
            return this.Url;
        }

        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class DateAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Date; } set { } }

        public override string ToString()
        {
            return this.Date;
        }

        [JsonProperty("date")]
        public string Date { get; set; }
    }

    public class DateTimeAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.DateTime; } set { } }

        public override string ToString()
        {
            return this.DateTime;
        }

        [JsonProperty("dateTime")]
        public string DateTime { get; set; }
    }

    public class PictureAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Picture; } set { } }

        public override string ToString()
        {
            return this.Picture;
        }

        [JsonProperty("picture")]
        public string Picture { get; set; }
    }

    public class TwitterAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Twitter; } set { } }

        public override string ToString()
        {
            return this.Twitter;
        }

        [JsonProperty("twitter")]
        public string Twitter { get; set; }
    }

    public class FaceBookAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.FaceBook; } set { } }

        public override string ToString()
        {
            return this.FaceBook;
        }

        [JsonProperty("facebook")]
        public string FaceBook { get; set; }
    }

    public class GoogleAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Google; } set { } }

        public override string ToString()
        {
            return this.Google;
        }

        [JsonProperty("google")]
        public string Google { get; set; }
    }

    public class CreditCardAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.CreditCard; } set { } }

        public override string ToString()
        {
            return this.Number + " " + this.Expiration + " " + this.Cvc;
        }

        [JsonProperty("number")]
        public string Number { get; set; }

        [JsonProperty("expiration")]
        public string Expiration { get; set; }

        [JsonProperty("cvc")]
        public string Cvc { get; set; }
    }

    public class PhoneAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Phone; } set { } }

        public override string ToString()
        {
            return this.CountryCode + " " + this.Number;
        }

        [JsonProperty("countryCode")]
        public string CountryCode { get; set; }

        [JsonProperty("number")]
        public string Number { get; set; }
    }


    public class AddressAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Address; } set { } }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(this.DisplayAddress) || string.IsNullOrWhiteSpace(this.DisplayAddress))
                return this.Street + " " + this.Extension + "\n" +
                       this.POBox + "\n" +
                       this.Locality + " " + this.PostCode + " " + this.Region + "\n" +
                       this.Country + "\n" +
                       this.Coordinates;
            else
                return this.DisplayAddress;
        }

        [JsonProperty("displayAddress")]
        public string DisplayAddress { get; set; }

        [JsonProperty("street")]
        public string Street { get; set; }

        [JsonProperty("extension")]
        public string Extension { get; set; }

        [JsonProperty("locality")]
        public string Locality { get; set; }

        [JsonProperty("region")]
        public string Region { get; set; }

        [JsonProperty("postOfficeBox")]
        public string POBox { get; set; }

        [JsonProperty("postCode")]
        public string PostCode { get; set; }

        [JsonProperty("country")]
        public string Country { get; set; }

        [JsonProperty("coordinates")]
        public string Coordinates { get; set; }
    }


    /// <summary>
    /// Summary description for Class1
    /// </summary>
    public class IdentityAttribute<T> where T : BaseAttribute
    {
        private T _value;
        private string _label;

        // User defined, e.g. vacation home, grocery credit card - or should the label be part of the value?
        public string Label { get { return _label; } set { _label = Label; } }

        // Class , e.g. credit card #, CVC, exp date
        public T Value { get { return _value; } set { _value = Value; } }

        // Obsoleted public AccessControlList<PermissionFlags> _acl = new AccessControlList<PermissionFlags>();

        public IdentityAttribute(string label, T obj)
        {
            _label = label;
            _value = obj;
        }

        public static explicit operator IdentityAttribute<T>(IdentityAttribute<NameAttribute> v)
        {
            throw new NotImplementedException();
        }
    }
}
