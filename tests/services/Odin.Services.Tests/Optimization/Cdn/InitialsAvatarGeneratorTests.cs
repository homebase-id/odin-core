using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Odin.Services.Optimization.Cdn;

namespace Odin.Services.Tests.Optimization.Cdn;

[TestFixture]
public class InitialsAvatarGeneratorTests
{
    private const string Seed = "frodo.dotyou.cloud";

    [Test]
    public void TryGenerate_GivenNameOnly_ProducesSingleInitial()
    {
        var ok = InitialsAvatarGenerator.TryGenerate("Frodo", null, Seed, out var pngBase64);

        Assert.That(ok, Is.True);
        var png = DecodePng(pngBase64!);
        Assert.That(png.Width, Is.EqualTo(250));
        Assert.That(png.Height, Is.EqualTo(250));
        Assert.That(png.ColorType, Is.EqualTo((byte)2), "expected 8-bit truecolor RGB");
        Assert.That(GetPixel(png, 0, 0), Is.Not.EqualTo((0xFF, 0xFF, 0xFF)), "background should be a palette color, not white");
        Assert.That(CountWhitePixels(png), Is.GreaterThan(0), "the glyph itself is drawn in white");
    }

    [Test]
    public void TryGenerate_GivenNameAndSurname_DrawsMoreThanSingleInitial()
    {
        InitialsAvatarGenerator.TryGenerate("Frodo", null, Seed, out var singleBase64);
        InitialsAvatarGenerator.TryGenerate("Frodo", "Baggins", Seed, out var doubleBase64);

        var singleWhite = CountWhitePixels(DecodePng(singleBase64!));
        var doubleWhite = CountWhitePixels(DecodePng(doubleBase64!));

        Assert.That(doubleWhite, Is.GreaterThan(singleWhite), "two initials should paint more white pixels than one");
    }

    [TestCase(null, null)]
    [TestCase("", "Baggins")]
    [TestCase("   ", "Baggins")]
    public void TryGenerate_NoGivenName_ReturnsFalse(string givenName, string surname)
    {
        var ok = InitialsAvatarGenerator.TryGenerate(givenName, surname, Seed, out var pngBase64);

        Assert.That(ok, Is.False);
        Assert.That(pngBase64, Is.Null);
    }

    [Test]
    public void TryGenerate_SameInputs_ProducesIdenticalBytesAcrossCalls()
    {
        InitialsAvatarGenerator.TryGenerate("Frodo", "Baggins", Seed, out var first);
        InitialsAvatarGenerator.TryGenerate("Frodo", "Baggins", Seed, out var second);

        // Guards against accidentally using string.GetHashCode() for the color seed (randomized
        // per-process in .NET) or any other non-determinism in the encoder -- the same identity must
        // always get the same avatar, including across process restarts.
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void TryGenerate_AccentedLetter_FoldsToBaseLatinLetter()
    {
        // "Émile" should fold (via Unicode NFD decomposition) to plain "E" -- the same first letter as
        // "Emile" -- so with the same seed the two must render byte-identical avatars.
        InitialsAvatarGenerator.TryGenerate("Émile", null, Seed, out var accented);
        InitialsAvatarGenerator.TryGenerate("Emile", null, Seed, out var plain);

        Assert.That(accented, Is.Not.Null);
        Assert.That(accented, Is.EqualTo(plain));
    }

    [Test]
    public void TryGenerate_LeadingEmojiBeforeLetters_SkipsToFirstLetter()
    {
        // A leading emoji before the real letters should be skipped entirely, producing the same
        // avatar as if it weren't there.
        InitialsAvatarGenerator.TryGenerate("🎉Frodo", null, Seed, out var withEmoji);
        InitialsAvatarGenerator.TryGenerate("Frodo", null, Seed, out var withoutEmoji);

        Assert.That(withEmoji, Is.Not.Null);
        Assert.That(withEmoji, Is.EqualTo(withoutEmoji));
    }

    [Test]
    public void TryGenerate_OnlyEmoji_ReturnsFalse()
    {
        var ok = InitialsAvatarGenerator.TryGenerate("🎉🎉🎉", null, Seed, out var pngBase64);

        Assert.That(ok, Is.False);
        Assert.That(pngBase64, Is.Null);
    }

    [Test]
    public void TryGenerate_NonLatinScript_ReturnsFalse()
    {
        // The built-in font only covers plain A-Z; a name whose first letter can't fold to that
        // alphabet (Cyrillic here) has no avatar to draw -- callers fall through to their own default.
        var ok = InitialsAvatarGenerator.TryGenerate("Фродо", null, Seed, out var pngBase64);

        Assert.That(ok, Is.False);
        Assert.That(pngBase64, Is.Null);
    }

    //
    // minimal PNG reader -- just enough to verify what the encoder produced
    //

    private readonly record struct DecodedPng(int Width, int Height, byte ColorType, byte[] RawScanlines);

    private static DecodedPng DecodePng(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var pos = 8; // skip the 8-byte PNG signature

        var width = 0;
        var height = 0;
        byte colorType = 0;
        using var idat = new MemoryStream();

        while (pos < bytes.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos, 4));
            var type = Encoding.ASCII.GetString(bytes, pos + 4, 4);
            var dataStart = pos + 8;

            switch (type)
            {
                case "IHDR":
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(dataStart, 4));
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(dataStart + 4, 4));
                    colorType = bytes[dataStart + 9];
                    break;
                case "IDAT":
                    idat.Write(bytes, dataStart, length);
                    break;
                case "IEND":
                    pos = bytes.Length;
                    continue;
            }

            pos = dataStart + length + 4; // skip chunk data + trailing CRC32
        }

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);

        return new DecodedPng(width, height, colorType, raw.ToArray());
    }

    private static (byte R, byte G, byte B) GetPixel(DecodedPng png, int x, int y)
    {
        var stride = 1 + png.Width * 3;
        var idx = y * stride + 1 + x * 3; // +1 skips the per-scanline filter-type byte
        return (png.RawScanlines[idx], png.RawScanlines[idx + 1], png.RawScanlines[idx + 2]);
    }

    private static int CountWhitePixels(DecodedPng png)
    {
        var stride = 1 + png.Width * 3;
        var count = 0;
        for (var y = 0; y < png.Height; y++)
        {
            var rowStart = y * stride + 1;
            for (var x = 0; x < png.Width; x++)
            {
                var idx = rowStart + x * 3;
                if (png.RawScanlines[idx] == 0xFF && png.RawScanlines[idx + 1] == 0xFF && png.RawScanlines[idx + 2] == 0xFF)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
