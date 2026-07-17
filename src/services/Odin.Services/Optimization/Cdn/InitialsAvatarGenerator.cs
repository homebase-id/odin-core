#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Odin.Services.Optimization.Cdn;

/// <summary>
/// Generates a simple initials-based PNG avatar (e.g. "JB") used as a personalized fallback for
/// <c>/pub/image</c> when an identity has an Anonymous-tier Name attribute but no published photo.
///
/// <para>
/// Hand-rolled rather than pulling in an image library: zlib compression comes from the BCL's
/// <see cref="ZLibStream"/> (built in since .NET 6), CRC32 is a ~15-line standard algorithm, and
/// glyphs are drawn from a tiny built-in 5x7 block font covering plain A-Z only. This mirrors the
/// existing project stance of avoiding an image-processing dependency (see
/// <c>ProfilePublishService.RepublishProfileImageAsync</c>) -- unlike resizing arbitrary uploaded
/// photos, drawing a solid background plus 1-2 letters on a fixed canvas is a small, bounded problem.
/// </para>
/// </summary>
public static class InitialsAvatarGenerator
{
    private const int CanvasSize = 250;
    private const int GlyphScale = 20; // each font pixel becomes a GlyphScale x GlyphScale block
    private const int GlyphCols = 5;
    private const int GlyphRows = 7;
    private const int GlyphGapCols = 1;

    // Curated palette (not raw random RGB) so every generated avatar has decent contrast against
    // the white glyph.
    private static readonly (byte R, byte G, byte B)[] Palette =
    [
        (0xF8, 0x71, 0x71), (0xFB, 0x92, 0x3C), (0xFB, 0xBF, 0x24), (0xA3, 0xE6, 0x35),
        (0x34, 0xD3, 0x99), (0x2D, 0xD4, 0xBF), (0x22, 0xD3, 0xEE), (0x60, 0xA5, 0xFA),
        (0x81, 0x8C, 0xF8), (0xA7, 0x8B, 0xFA), (0xE8, 0x79, 0xF9), (0xFB, 0x71, 0x85)
    ];

    /// <summary>
    /// Attempts to build an initials avatar from <paramref name="givenName"/>/<paramref name="surname"/>.
    /// Returns false (and a null <paramref name="pngBase64"/>) when there's no given name to work with,
    /// or its first letter can't be folded to plain A-Z (the built-in font's only alphabet) -- callers
    /// should fall through to their own default/generic fallback in either case.
    /// </summary>
    public static bool TryGenerate(string? givenName, string? surname, string colorSeed, out string? pngBase64)
    {
        var initials = ExtractInitials(givenName, surname);
        if (initials == null)
        {
            pngBase64 = null;
            return false;
        }

        var (r, g, b) = Palette[(int)(StableHash(colorSeed) % (uint)Palette.Length)];
        var pixels = RenderPixels(initials, r, g, b);
        pngBase64 = Convert.ToBase64String(EncodePng(pixels, CanvasSize, CanvasSize));
        return true;
    }

    //
    // initials extraction
    //

    private static string? ExtractInitials(string? givenName, string? surname)
    {
        var first = FirstLetter(givenName);
        if (first == null)
        {
            return null;
        }

        var second = FirstLetter(surname);
        return second == null ? first.Value.ToString() : $"{first}{second}";
    }

    // Folds accented Latin letters to their base form (e.g. "É" -> "E") via Unicode decomposition so
    // the plain-ASCII font can still render them, then takes the first resulting A-Z letter -- skipping
    // any leading non-letter elements (emoji, punctuation) and any letter outside plain Latin (Cyrillic,
    // CJK, etc., which the font simply can't represent).
    private static char? FirstLetter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var enumerator = StringInfo.GetTextElementEnumerator(normalized);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            if (element.Length == 0 || !char.IsLetter(element, 0))
            {
                continue;
            }

            var c = char.ToUpperInvariant(element[0]);
            if (c is >= 'A' and <= 'Z')
            {
                return c;
            }
        }

        return null;
    }

    // Deterministic across process restarts, unlike string.GetHashCode() (randomized per-process in
    // .NET) -- the same identity must always land on the same palette color.
    private static uint StableHash(string value)
    {
        const uint fnvOffsetBasis = 2166136261;
        const uint fnvPrime = 16777619;

        var hash = fnvOffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= fnvPrime;
        }

        return hash;
    }

    //
    // pixel rendering
    //

    private static byte[] RenderPixels(string initials, byte r, byte g, byte b)
    {
        var pixels = new byte[CanvasSize * CanvasSize * 3];
        for (var i = 0; i < CanvasSize * CanvasSize; i++)
        {
            pixels[i * 3] = r;
            pixels[i * 3 + 1] = g;
            pixels[i * 3 + 2] = b;
        }

        var glyphWidth = GlyphCols * GlyphScale;
        var glyphHeight = GlyphRows * GlyphScale;
        var gapWidth = GlyphGapCols * GlyphScale;
        var totalWidth = initials.Length == 2 ? glyphWidth * 2 + gapWidth : glyphWidth;

        var startX = (CanvasSize - totalWidth) / 2;
        var startY = (CanvasSize - glyphHeight) / 2;

        for (var i = 0; i < initials.Length; i++)
        {
            DrawGlyph(pixels, Font.Get(initials[i]), startX + i * (glyphWidth + gapWidth), startY);
        }

        return pixels;
    }

    private static void DrawGlyph(byte[] pixels, byte[] glyphRows, int offsetX, int offsetY)
    {
        for (var row = 0; row < GlyphRows; row++)
        {
            var bits = glyphRows[row];
            for (var col = 0; col < GlyphCols; col++)
            {
                if ((bits & (1 << (GlyphCols - 1 - col))) == 0)
                {
                    continue;
                }

                var px0 = offsetX + col * GlyphScale;
                var py0 = offsetY + row * GlyphScale;
                for (var dy = 0; dy < GlyphScale; dy++)
                {
                    var py = py0 + dy;
                    if (py < 0 || py >= CanvasSize)
                    {
                        continue;
                    }

                    for (var dx = 0; dx < GlyphScale; dx++)
                    {
                        var px = px0 + dx;
                        if (px < 0 || px >= CanvasSize)
                        {
                            continue;
                        }

                        var idx = (py * CanvasSize + px) * 3;
                        pixels[idx] = 0xFF;
                        pixels[idx + 1] = 0xFF;
                        pixels[idx + 2] = 0xFF;
                    }
                }
            }
        }
    }

    //
    // tiny built-in 5x7 block font, A-Z only
    //

    private static class Font
    {
        // Each glyph is 7 rows; each row uses the low 5 bits (bit 4 = leftmost column).
        private static readonly Dictionary<char, byte[]> Glyphs = new()
        {
            ['A'] = Rows("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
            ['B'] = Rows("11110", "10001", "10001", "11110", "10001", "10001", "11110"),
            ['C'] = Rows("01111", "10000", "10000", "10000", "10000", "10000", "01111"),
            ['D'] = Rows("11110", "10001", "10001", "10001", "10001", "10001", "11110"),
            ['E'] = Rows("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
            ['F'] = Rows("11111", "10000", "10000", "11110", "10000", "10000", "10000"),
            ['G'] = Rows("01111", "10000", "10000", "10111", "10001", "10001", "01111"),
            ['H'] = Rows("10001", "10001", "10001", "11111", "10001", "10001", "10001"),
            ['I'] = Rows("11111", "00100", "00100", "00100", "00100", "00100", "11111"),
            ['J'] = Rows("00111", "00010", "00010", "00010", "00010", "10010", "01100"),
            ['K'] = Rows("10001", "10010", "10100", "11000", "10100", "10010", "10001"),
            ['L'] = Rows("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
            ['M'] = Rows("10001", "11011", "10101", "10001", "10001", "10001", "10001"),
            ['N'] = Rows("10001", "11001", "10101", "10011", "10001", "10001", "10001"),
            ['O'] = Rows("01110", "10001", "10001", "10001", "10001", "10001", "01110"),
            ['P'] = Rows("11110", "10001", "10001", "11110", "10000", "10000", "10000"),
            ['Q'] = Rows("01110", "10001", "10001", "10001", "10101", "10010", "01101"),
            ['R'] = Rows("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
            ['S'] = Rows("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
            ['T'] = Rows("11111", "00100", "00100", "00100", "00100", "00100", "00100"),
            ['U'] = Rows("10001", "10001", "10001", "10001", "10001", "10001", "01110"),
            ['V'] = Rows("10001", "10001", "10001", "10001", "10001", "01010", "00100"),
            ['W'] = Rows("10001", "10001", "10001", "10101", "10101", "11011", "10001"),
            ['X'] = Rows("10001", "10001", "01010", "00100", "01010", "10001", "10001"),
            ['Y'] = Rows("10001", "10001", "01010", "00100", "00100", "00100", "00100"),
            ['Z'] = Rows("11111", "00001", "00010", "00100", "01000", "10000", "11111"),
        };

        public static byte[] Get(char c) => Glyphs.TryGetValue(c, out var glyph) ? glyph : Glyphs['O'];

        private static byte[] Rows(params string[] rows)
        {
            var result = new byte[rows.Length];
            for (var i = 0; i < rows.Length; i++)
            {
                byte b = 0;
                foreach (var ch in rows[i])
                {
                    b = (byte)((b << 1) | (ch == '1' ? 1 : 0));
                }

                result[i] = b;
            }

            return result;
        }
    }

    //
    // minimal PNG encoder (8-bit truecolor RGB, no interlacing, single IDAT chunk)
    //

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] EncodePng(byte[] rgbPixels, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write(PngSignature);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // color type: truecolor (RGB, no alpha)
        ihdr[10] = 0; // compression method
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // interlace method
        WriteChunk(output, "IHDR", ihdr);

        var stride = 1 + width * 3;
        var raw = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            raw[y * stride] = 0; // per-scanline filter type: None
            Buffer.BlockCopy(rgbPixels, y * width * 3, raw, y * stride + 1, width * 3);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);

        Span<byte> lengthBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBuf, (uint)data.Length);
        stream.Write(lengthBuf);

        stream.Write(typeBytes);
        stream.Write(data);

        Span<byte> crcBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBuf, Crc32(typeBytes, data));
        stream.Write(crcBuf);
    }

    //
    // CRC32 (standard PNG/zlib polynomial), hand-rolled to avoid a package dependency
    //

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] typeBytes, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in typeBytes)
        {
            c = Crc32Table[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        foreach (var b in data)
        {
            c = Crc32Table[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFFu;
    }
}
