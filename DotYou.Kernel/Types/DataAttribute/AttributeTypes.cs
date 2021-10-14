namespace DotYou.Types.DataAttribute
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
}
