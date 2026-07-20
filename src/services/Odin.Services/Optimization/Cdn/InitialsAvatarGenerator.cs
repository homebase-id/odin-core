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
/// glyphs are rasterized from an embedded DejaVu Sans font via <see cref="TrueTypeFont"/> -- a
/// minimal hand-written TrueType outline parser and scanline rasterizer, rather than a real glyph
/// bitmap font. This mirrors the existing project stance of avoiding an image-processing dependency
/// (see <c>ProfilePublishService.RepublishProfileImageAsync</c>) -- unlike resizing arbitrary
/// uploaded photos, drawing a solid background plus 1-2 letters on a fixed canvas is a small,
/// bounded problem.
/// </para>
/// </summary>
public static class InitialsAvatarGenerator
{
    private const int CanvasSize = 250;
    private const int CapHeightPx = 140; // target height of a capital letter, in canvas pixels
    private const int MarginPx = 14; // minimum breathing room reserved on each side of the canvas
    private const float GapRatio = 0.15f; // horizontal gap between two initials, as a fraction of cap height
    private const int SuperSample = 4; // AA samples per axis per output pixel (SuperSample^2 total)

    // The exact "lightTheme" palette from odin-js's OdinIdColorValues (getOdinIdColor,
    // packages/common/common-app/src/helpers/colors/hostnameColors.ts), reproduced verbatim so the
    // avatar background shown here matches the one odin-js's own FallbackImg component shows
    // elsewhere in the app for the same identity. Selection happens in OdinIdColorIndex below.
    internal static readonly string[] LightThemeHexPalette =
    [
        "#006da3", "#007a3d", "#c13215", "#b814b8", "#5b6976", "#3d7406", "#cc0066", "#2e51ff",
        "#9c5711", "#007575", "#d00b4d", "#8f2af4", "#d00b0b", "#067906", "#5151f6", "#866118",
        "#067953", "#a20ced", "#4b7000", "#c70a88", "#b34209", "#06792d", "#7a3df5", "#6b6b24",
        "#d00b2c", "#2d7906", "#af0bd0", "#32763e", "#2662d9", "#76681e", "#067462", "#6447f5",
        "#5e6e0c", "#077288", "#c20aa3", "#2d761e"
    ];

    internal static readonly (byte R, byte G, byte B)[] Palette = Array.ConvertAll(LightThemeHexPalette, ParseHexColor);

    private static (byte R, byte G, byte B) ParseHexColor(string hex) => (
        Convert.ToByte(hex.Substring(1, 2), 16),
        Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16));

    /// <summary>
    /// Attempts to build an initials avatar from <paramref name="givenName"/>/<paramref name="surname"/>.
    /// Returns false (and a null <paramref name="pngBase64"/>) when there's no given name to work with,
    /// or its first letter can't be folded to plain A-Z (the built-in font's only alphabet) -- callers
    /// should fall through to <see cref="TryGenerateFromDomain"/> (or their own default) in either case.
    /// </summary>
    public static bool TryGenerate(string? givenName, string? surname, string colorSeed, out string? pngBase64)
    {
        var initials = ExtractInitials(givenName, surname);
        if (initials == null)
        {
            pngBase64 = null;
            return false;
        }

        pngBase64 = GenerateCore(initials, colorSeed);
        return true;
    }

    /// <summary>
    /// Ported from odin-js's getTwoLettersFromDomain (used by FallbackImg.tsx as the last-resort
    /// avatar when there's no name to derive initials from either): two letters taken directly from
    /// the domain itself, so there's essentially always something more personal than a blank/generic
    /// silhouette to show. Only fails (returns false) for a null/empty domain.
    /// </summary>
    public static bool TryGenerateFromDomain(string? domain, out string? pngBase64)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            pngBase64 = null;
            return false;
        }

        var initials = GetTwoLettersFromDomain(domain).ToUpperInvariant();
        if (initials.Length == 0)
        {
            pngBase64 = null;
            return false;
        }

        pngBase64 = GenerateCore(initials, domain);
        return true;
    }

    private static string GenerateCore(string initials, string colorSeed)
    {
        var (r, g, b) = Palette[(int)OdinIdColorIndex(colorSeed)];
        var pixels = RenderPixels(initials, r, g, b);
        return Convert.ToBase64String(EncodePng(pixels, CanvasSize, CanvasSize));
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

    // Ported from odin-js's getTwoLettersFromDomain (packages/libs/js-lib/src/helpers/DomainUtil.ts).
    // Unlike FirstLetter above, this takes raw characters as-is (no letter filtering, no diacritic
    // folding) -- matching JS's clamped substring() (never throws on a too-short part) rather than
    // C#'s Substring (throws past the end), since e.g. a single-label domain part can be 1 character.
    // Internal (rather than private) so tests can assert its output directly against odin-js.
    internal static string GetTwoLettersFromDomain(string domain)
    {
        var parts = domain.Replace("www.", "").Split('.');
        if (parts.Length <= 2)
        {
            var part = parts[0];
            return part.Length <= 2 ? part : part.Substring(0, 2);
        }

        var first = parts[0].Length > 0 ? parts[0].Substring(0, 1) : "";
        var second = parts[1].Length > 0 ? parts[1].Substring(0, 1) : "";
        return first + second;
    }

    // Ported verbatim from odin-js's getOdinIdColor (same file as the palette above): XOR every
    // UTF-16 code unit of the OdinId together, then index into the palette mod its length. Matching
    // this exactly (not e.g. FNV-1a) is what makes the two implementations agree on a color for the
    // same identity -- char in C# is already a UTF-16 code unit, same as JS's charCodeAt.
    private static uint OdinIdColorIndex(string odinId)
    {
        uint c = 0;
        foreach (var ch in odinId)
        {
            c ^= ch;
        }

        return c % (uint)Palette.Length;
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

        var font = TrueTypeFont.DejaVuSans;
        var glyphs = new List<TrueTypeFont.Glyph>(initials.Length);
        foreach (var ch in initials)
        {
            if (font.TryGetGlyph(ch, out var glyph) || font.TryGetGlyph('O', out glyph))
            {
                glyphs.Add(glyph);
            }
        }

        if (glyphs.Count == 0)
        {
            return pixels;
        }

        // DejaVu Sans is proportionally spaced (unlike the old fixed-width bitmap font), so a wide pair
        // like "WQ" can be much wider than a narrow pair like "IJ" at the same cap height. Compute width
        // in font units first, and shrink the scale below the cap-height target if needed so the widest
        // pairs still fit inside the canvas with margin.
        var gapUnits = font.CapHeightUnits * GapRatio;
        var minY = int.MaxValue;
        var maxY = int.MinValue;
        var totalWidthUnits = gapUnits * (glyphs.Count - 1);
        foreach (var glyph in glyphs)
        {
            minY = Math.Min(minY, glyph.YMin);
            maxY = Math.Max(maxY, glyph.YMax);
            totalWidthUnits += glyph.XMax - glyph.XMin;
        }

        var heightScale = CapHeightPx / (float)font.CapHeightUnits;
        var maxWidthPx = CanvasSize - 2 * MarginPx;
        var widthScale = maxWidthPx / totalWidthUnits;
        var scale = Math.Min(heightScale, widthScale);

        var totalWidth = totalWidthUnits * scale;
        var scaledHeight = (maxY - minY) * scale;
        var startX = (CanvasSize - totalWidth) / 2f;
        var startY = (CanvasSize - scaledHeight) / 2f;
        var baselinePixelY = startY + maxY * scale; // shared baseline so multiple glyphs align typographically

        var penX = startX;
        foreach (var glyph in glyphs)
        {
            var pixelXOffset = penX - glyph.XMin * scale;
            DrawGlyph(pixels, glyph, scale, pixelXOffset, baselinePixelY);
            penX += (glyph.XMax - glyph.XMin) * scale + gapUnits * scale;
        }

        return pixels;
    }

    // Rasterizes one glyph's outline (font-unit contours, transformed to pixel space) via a classic
    // nonzero-winding scanline fill, point-sampled at SuperSample^2 positions per output pixel for
    // antialiasing, then alpha-blends white over whatever's already in the pixel buffer (the solid
    // background color).
    private static void DrawGlyph(byte[] pixels, TrueTypeFont.Glyph glyph, float scale, float pixelXOffset, float baselinePixelY)
    {
        var edges = new List<(float X0, float Y0, float X1, float Y1)>();
        var minPX = float.MaxValue;
        var maxPX = float.MinValue;
        var minPY = float.MaxValue;
        var maxPY = float.MinValue;

        foreach (var contour in glyph.Contours)
        {
            for (var i = 0; i < contour.Count; i++)
            {
                var p0 = contour[i];
                var p1 = contour[(i + 1) % contour.Count];
                var x0 = pixelXOffset + p0.X * scale;
                var y0 = baselinePixelY - p0.Y * scale;
                var x1 = pixelXOffset + p1.X * scale;
                var y1 = baselinePixelY - p1.Y * scale;
                edges.Add((x0, y0, x1, y1));
                minPX = Math.Min(minPX, Math.Min(x0, x1));
                maxPX = Math.Max(maxPX, Math.Max(x0, x1));
                minPY = Math.Min(minPY, Math.Min(y0, y1));
                maxPY = Math.Max(maxPY, Math.Max(y0, y1));
            }
        }

        if (edges.Count == 0)
        {
            return;
        }

        var xStart = Math.Max(0, (int)MathF.Floor(minPX));
        var xEnd = Math.Min(CanvasSize - 1, (int)MathF.Ceiling(maxPX));
        var yStart = Math.Max(0, (int)MathF.Floor(minPY));
        var yEnd = Math.Min(CanvasSize - 1, (int)MathF.Ceiling(maxPY));
        if (xEnd < xStart || yEnd < yStart)
        {
            return;
        }

        var width = xEnd - xStart + 1;
        var coverage = new int[width];
        var crossings = new List<(float X, int Direction)>();

        for (var py = yStart; py <= yEnd; py++)
        {
            Array.Clear(coverage, 0, width);

            for (var sy = 0; sy < SuperSample; sy++)
            {
                var sampleY = py + (sy + 0.5f) / SuperSample;
                crossings.Clear();
                foreach (var (ex0, ey0, ex1, ey1) in edges)
                {
                    if (ey0 == ey1)
                    {
                        continue;
                    }

                    var edgeYMin = Math.Min(ey0, ey1);
                    var edgeYMax = Math.Max(ey0, ey1);
                    if (sampleY < edgeYMin || sampleY >= edgeYMax)
                    {
                        continue;
                    }

                    var t = (sampleY - ey0) / (ey1 - ey0);
                    crossings.Add((ex0 + t * (ex1 - ex0), ey1 > ey0 ? 1 : -1));
                }

                if (crossings.Count < 2)
                {
                    continue;
                }

                crossings.Sort((a, b) => a.X.CompareTo(b.X));

                var winding = 0;
                for (var ci = 0; ci < crossings.Count - 1; ci++)
                {
                    winding += crossings[ci].Direction;
                    if (winding != 0)
                    {
                        AccumulateSpan(coverage, xStart, width, crossings[ci].X, crossings[ci + 1].X);
                    }
                }
            }

            var rowStart = py * CanvasSize;
            for (var i = 0; i < width; i++)
            {
                if (coverage[i] == 0)
                {
                    continue;
                }

                var alpha = coverage[i] / (float)(SuperSample * SuperSample);
                var idx = (rowStart + xStart + i) * 3;
                pixels[idx] = (byte)MathF.Round(pixels[idx] + (255 - pixels[idx]) * alpha);
                pixels[idx + 1] = (byte)MathF.Round(pixels[idx + 1] + (255 - pixels[idx + 1]) * alpha);
                pixels[idx + 2] = (byte)MathF.Round(pixels[idx + 2] + (255 - pixels[idx + 2]) * alpha);
            }
        }
    }

    // Point-samples the horizontal span [spanStart, spanEnd) at SuperSample sub-column positions per
    // pixel, adding a hit count into coverage[] (indexed relative to xStart) for each sub-sample that
    // falls inside the span.
    private static void AccumulateSpan(int[] coverage, int xStart, int width, float spanStart, float spanEnd)
    {
        if (spanEnd <= spanStart)
        {
            return;
        }

        var pxFrom = Math.Max(xStart, (int)MathF.Floor(spanStart));
        var pxTo = Math.Min(xStart + width - 1, (int)MathF.Floor(spanEnd - 1e-4f));
        for (var px = pxFrom; px <= pxTo; px++)
        {
            for (var sx = 0; sx < SuperSample; sx++)
            {
                var sampleX = px + (sx + 0.5f) / SuperSample;
                if (sampleX >= spanStart && sampleX < spanEnd)
                {
                    coverage[px - xStart]++;
                }
            }
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
