using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Core.Cryptography.Tests;

public class TestContentHash
{
    // BLAKE3 values are the first 32 bytes of "hash" in the official test vectors
    // (github.com/BLAKE3-team/BLAKE3, test_vectors/test_vectors.json). SHA-256 values were computed
    // independently with Python hashlib. Input is the repeating sequence 0, 1, ..., 250, 0, 1, ...
    [TestCase(0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262")]
    [TestCase(1, "6e340b9cffb37a989ca544e6bb780a2c78901d3fb33738768511a30617afa01d", "2d3adedff11b61f14c886e35afa036736dcd87a74d27b5c1510225d0f592e213")]
    [TestCase(2, "b413f47d13ee2fe6c845b2ee141af81de858df4ec549a58b7970bb96645bc8d2", "7b7015bb92cf0b318037702a6cdd81dee41224f734684c2c122cd6359cb1ee63")]
    [TestCase(3, "ae4b3280e56e2faf83f414a6e3dabe9d5fbe18976544c05fed121accb85b53fc", "e1be4d7a8ab5560aa4199eea339849ba8e293d55ca0a81006726d184519e647f")]
    [TestCase(4, "054edec1d0211f624fed0cbca9d4f9400b0e491c43742af2c5b0abebf0c990d8", "f30f5ab28fe047904037f77b6da4fea1e27241c5d132638d8bedce9d40494f32")]
    [TestCase(5, "08bb5e5d6eaac1049ede0893d30ed022b1a4d9b5b48db414871f51c9cb35283d", "b40b44dfd97e7a84a996a91af8b85188c66c126940ba7aad2e7ae6b385402aa2")]
    [TestCase(6, "17e88db187afd62c16e5debf3e6527cd006bc012bc90b51a810cd80c2d511f43", "06c4e8ffb6872fad96f9aaca5eee1553eb62aed0ad7198cef42e87f6a616c844")]
    [TestCase(7, "57355ac3303c148f11aef7cb179456b9232cde33a818dfda2c2fcb9325749a6b", "3f8770f387faad08faa9d8414e9f449ac68e6ff0417f673f602a646a891419fe")]
    [TestCase(8, "8a851ff82ee7048ad09ec3847f1ddf44944104d2cbd17ef4e3db22c6785a0d45", "2351207d04fc16ade43ccab08600939c7c1fa70a5c0aaca76063d04c3228eaeb")]
    [TestCase(63, "29af2686fd53374a36b0846694cc342177e428d1647515f078784d69cdb9e488", "e9bc37a594daad83be9470df7f7b3798297c3d834ce80ba85d6e207627b7db7b")]
    [TestCase(64, "fdeab9acf3710362bd2658cdc9a29e8f9c757fcf9811603a8c447cd1d9151108", "4eed7141ea4a5cd4b788606bd23f46e212af9cacebacdc7d1f4c6dc7f2511b98")]
    [TestCase(65, "4bfd2c8b6f1eec7a2afeb48b934ee4b2694182027e6d0fc075074f2fabb31781", "de1e5fa0be70df6d2be8fffd0e99ceaa8eb6e8c93a63f2d8d1c30ecb6b263dee")]
    [TestCase(127, "92ca0fa6651ee2f97b884b7246a562fa71250fedefe5ebf270d31c546bfea976", "d81293fda863f008c09e92fc382a81f5a0b4a1251cba1634016a0f86a6bd640d")]
    [TestCase(128, "471fb943aa23c511f6f72f8d1652d9c880cfa392ad80503120547703e56a2be5", "f17e570564b26578c33bb7f44643f539624b05df1a76c81f30acd548c44b45ef")]
    [TestCase(129, "5099c6a56203f9687f7d33f4bfdf576d31dc91f6b695ecea38b2770c87631135", "683aaae9f3c5ba37eaaf072aed0f9e30bac0865137bae68b1fde4ca2aebdcb12")]
    [TestCase(1023, "1c5e88a585b61754df6137d66632a7348557a88358afc401b0a0a4fc427104a9", "10108970eeda3eb932baac1428c7a2163b0e924c9a9e25b35bba72b28f70bd11")]
    [TestCase(1024, "2bce1ba628720664be4b9fdd77aae0678e5f0f3f02fc6ff641ec879094f6a404", "42214739f095a406f3fc83deb889744ac00df831c10daa55189b5d121c855af7")]
    [TestCase(1025, "bc0b6b10b89b9487a12fda2a8cc13194e7091c217aabf8b92846274026f4bcd0", "d00278ae47eb27b34faecf67b4fe263f82d5412916c1ffd97c8cb7fb814b8444")]
    [TestCase(2048, "b2a8170614e23194ae2951423d601987f518ce2f11205d7b0b708080103b9f76", "e776b6028c7cd22a4d0ba182a8bf62205d2ef576467e838ed6f2529b85fba24a")]
    [TestCase(2049, "26e1e2808e3a6cf967ca03f6749a063c5ed55f92f5874653a1faabed78346f00", "5f4d72f40d7a5f82b15ca2b2e44b1de3c2ef86c426c95c1af0b6879522563030")]
    [TestCase(3072, "5f24b2f16026ec7d0450a5a08283d3cfd47302fe859f579ed79fe7d2663b73f9", "b98cb0ff3623be03326b373de6b9095218513e64f1ee2edd2525c7ad1e5cffd2")]
    [TestCase(3073, "b870cdfe188c14fbfc31a1be12cd7e83b63551fff30f847fa275d5d4ac409471", "7124b49501012f81cc7f11ca069ec9226cecb8a2c850cfe644e327d22d3e1cd3")]
    [TestCase(4096, "d67c656e01756650d77717b0839985a056ec28ffe174601d690fc407a2ceffca", "015094013f57a5277b59d8475c0501042c0b642e531b0a1c8f58d2163229e969")]
    [TestCase(4097, "a16560d668b843fb3be99ace41dbd18471f342bd3255a1d21204b35e43f74436", "9b4052b38f1c5fc8b1f9ff7ac7b27cd242487b3d890d15c96a1c25b8aa0fb995")]
    [TestCase(5120, "2d3fb9161493509e3fa3f5472d8a284ee687f64524f0925be67e132ef43f43e0", "9cadc15fed8b5d854562b26a9536d9707cadeda9b143978f319ab34230535833")]
    [TestCase(5121, "f19db61046ca889db9bd34d779cd362c6a7198cb5a0e3a2882e7bd24a901cc1d", "628bd2cb2004694adaab7bbd778a25df25c47b9d4155a55f8fbd79f2fe154cff")]
    [TestCase(6144, "b7806fa749a8944b54898488d9cf0bcbd8d8010eaa4955b9aaa809a4100953bd", "3e2e5b74e048f3add6d21faab3f83aa44d3b2278afb83b80b3c35164ebeca205")]
    [TestCase(6145, "14c3dded0a72fea7b6032b1648b3018bd7174dc2b210b94de258e06ad76533bb", "f1323a8631446cc50536a9f705ee5cb619424d46887f3c376c695b70e0f0507f")]
    [TestCase(7168, "fc886a975cb8681be6e3693287b09735d2493d274a8cf305de38f4aafa5f72d8", "61da957ec2499a95d6b8023e2b0e604ec7f6b50e80a9678b89d2628e99ada77a")]
    [TestCase(7169, "ca9fb85c318e92457d00499e940dee1e25dd928cb5804f939e4856d52508aa56", "a003fc7a51754a9b3c7fae0367ab3d782dccf28855a03d435f8cfe74605e7817")]
    [TestCase(8192, "25df2449b2e5a35fea14e02a7158e283801a1069c9f84631b9a9dacb2f809a7f", "aae792484c8efe4f19e2ca7d371d8c467ffb10748d8a5a1ae579948f718a2a63")]
    [TestCase(8193, "7e3691790cd64b19d4edb1a80e988214515abeb53aa0f34ffbfe4b4bf405d120", "bab6c09cb8ce8cf459261398d2e7aef35700bf488116ceb94a36d0f5f1b7bc3b")]
    [TestCase(16384, "4348e3b98e8a327b34ced39c1da9e67cdb4cd5e48e4d7960607a3ae403d35f0c", "f875d6646de28985646f34ee13be9a576fd515f76b5b0a26bb324735041ddde4")]
    [TestCase(31744, "3cfe29c8d109f9f2c47826c78f931f31fdec70a2cf0ddfbba8fe8009a729dd42", "62b6960e1a44bcc1eb1a611a8d6235b6b4b78f32e7abc4fb4c6cdcce94895c47")]
    [TestCase(102400, "74588b7f0bcc354ac14d9cf199fa3a20c05f0c7293b9075b2f2e146e718de800", "bc3e3d41a1146b069abffad3c0d44860cf664390afce4d9661f7902e7943e085")]
    public async Task HashesMatchTheReferenceVectors(int length, string sha256Hex, string blake3Hex)
    {
        var input = Input(length);

        Assert.That(Convert.ToHexStringLower(IncrementalContentHash.Compute(ContentHashAlgorithm.Sha256, input)), Is.EqualTo(sha256Hex));
        Assert.That(Convert.ToHexStringLower(IncrementalContentHash.Compute(ContentHashAlgorithm.Blake3, input)), Is.EqualTo(blake3Hex));

        // Odd read sizes cross every internal block boundary of both algorithms
        Assert.That(await HashThroughStream(input, ContentHashAlgorithm.Sha256, readSize: 7), Is.EqualTo(sha256Hex));
        Assert.That(await HashThroughStream(input, ContentHashAlgorithm.Blake3, readSize: 7), Is.EqualTo(blake3Hex));
        Assert.That(await HashThroughStream(input, ContentHashAlgorithm.Blake3, readSize: 1 << 20), Is.EqualTo(blake3Hex));
    }

    [Test]
    public void HashingReadStreamForwardsLengthAndIsNotSeekable()
    {
        using var inner = new MemoryStream(Input(1000));
        using var hashing = new HashingReadStream(inner, ContentHashAlgorithm.Blake3);

        Assert.That(hashing.Length, Is.EqualTo(1000));
        Assert.That(hashing.CanSeek, Is.False);
        Assert.Throws<NotSupportedException>(() => hashing.Seek(0, SeekOrigin.Begin));
    }

    [Test]
    public void HashingReadStreamDoesNotDisposeTheInnerStream()
    {
        var inner = new MemoryStream(Input(10));
        new HashingReadStream(inner, ContentHashAlgorithm.Sha256).Dispose();

        Assert.That(inner.CanRead, Is.True);
    }

    [Test]
    public void UnknownAlgorithmIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new IncrementalContentHash((ContentHashAlgorithm)0));
    }

    private static async Task<string> HashThroughStream(byte[] input, ContentHashAlgorithm algorithm, int readSize)
    {
        await using var hashing = new HashingReadStream(new MemoryStream(input), algorithm);
        var buffer = new byte[readSize];
        while (await hashing.ReadAsync(buffer) > 0)
        {
        }

        Assert.That(hashing.Position, Is.EqualTo(input.Length));
        return Convert.ToHexStringLower(hashing.GetHash());
    }

    private static byte[] Input(int length)
    {
        var input = new byte[length];
        for (var i = 0; i < length; i++)
        {
            input[i] = (byte)(i % 251);
        }

        return input;
    }
}
