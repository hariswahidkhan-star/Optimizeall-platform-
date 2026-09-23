using System.Buffers.Binary;

namespace OptimizeAll.Api.Modules.Files;

/// <summary>
/// Removes privacy-sensitive metadata (EXIF incl. GPS, XMP, IPTC, text comments, timestamps) from uploaded images by
/// dropping container segments/chunks. Pixel data is never decoded or re-encoded, so the image is bit-identical
/// otherwise. Pure and side-effect free.
/// <list type="bullet">
/// <item>JPEG: drops APP1 (Exif/XMP), APP13 (Photoshop/IPTC) and COM segments before the first scan; keeps APP0
/// (JFIF), APP2 (ICC profile) and everything else.</item>
/// <item>PNG: drops eXIf, tEXt, zTXt, iTXt and tIME chunks.</item>
/// <item>WebP: drops EXIF and "XMP " chunks, rewrites the RIFF size and clears the VP8X EXIF/XMP flags.</item>
/// </list>
/// Parsing is lenient: when the container structure stops making sense (a truncated or corrupt tail) the remaining
/// bytes are kept unchanged, because the image was already accepted by <see cref="Domain.Files.ImageInspector"/>.
/// </summary>
public static class ImageMetadataStripper
{
    private static ReadOnlySpan<byte> PngSignature => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly HashSet<string> PngMetadataChunks = new(StringComparer.Ordinal) { "eXIf", "tEXt", "zTXt", "iTXt", "tIME" };

    /// <summary>The image without metadata, or the input unchanged when it has none (or is not PNG/JPEG/WebP).</summary>
    public static byte[] Strip(byte[] data)
    {
        if (data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(PngSignature)) return StripPng(data);
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return StripJpeg(data);
        if (data.Length >= 12 && Ascii(data, 0, "RIFF") && Ascii(data, 8, "WEBP")) return StripWebP(data);
        return data;
    }

    // ------------------------------------------------------------------ JPEG

    private static byte[] StripJpeg(byte[] d)
    {
        using var output = new MemoryStream(d.Length);
        output.Write(d, 0, 2); // SOI
        var i = 2;
        var removed = false;
        while (i < d.Length)
        {
            if (d[i] != 0xFF || i + 1 >= d.Length) break; // not a marker: keep the rest as-is
            var marker = d[i + 1];
            if (marker == 0xFF) { output.WriteByte(0xFF); i++; continue; } // fill byte
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { output.Write(d, i, 2); i += 2; continue; } // no length
            if (marker is 0xD9 or 0xDA) break; // EOI / start of scan: entropy-coded data follows, keep verbatim
            if (i + 4 > d.Length) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(i + 2, 2));
            if (length < 2 || i + 2 + length > d.Length) break;

            var drop = marker is 0xE1 or 0xED or 0xFE; // APP1 (Exif/XMP), APP13 (IPTC), COM
            if (drop) removed = true;
            else output.Write(d, i, 2 + length);
            i += 2 + length;
        }
        if (!removed) return d;
        if (i < d.Length) output.Write(d, i, d.Length - i);
        return output.ToArray();
    }

    // ------------------------------------------------------------------ PNG

    private static byte[] StripPng(byte[] d)
    {
        using var output = new MemoryStream(d.Length);
        output.Write(d, 0, 8);
        var i = 8;
        var removed = false;
        while (i + 12 <= d.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(i, 4));
            var total = 12L + length; // length + type + data + CRC
            if (length > int.MaxValue || i + total > d.Length || !IsChunkType(d, i + 4)) break;

            var type = System.Text.Encoding.ASCII.GetString(d, i + 4, 4);
            if (PngMetadataChunks.Contains(type)) removed = true;
            else output.Write(d, i, (int)total);
            i += (int)total;
            if (type == "IEND") break;
        }
        if (!removed) return d;
        if (i < d.Length) output.Write(d, i, d.Length - i);
        return output.ToArray();
    }

    private static bool IsChunkType(byte[] d, int offset)
    {
        for (var k = 0; k < 4; k++)
            if (!char.IsAsciiLetter((char)d[offset + k])) return false;
        return true;
    }

    // ------------------------------------------------------------------ WebP

    private const byte VP8XExifFlag = 0x08;
    private const byte VP8XXmpFlag = 0x04;

    private static byte[] StripWebP(byte[] d)
    {
        using var output = new MemoryStream(d.Length);
        output.Write(d, 0, 12); // "RIFF" size "WEBP" (size is rewritten below)
        var i = 12;
        var removed = false;
        var vp8xFlagsOffset = -1;
        while (i + 8 <= d.Length)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(i + 4, 4));
            var total = 8L + size + (size & 1); // chunks are padded to an even size
            if (size > int.MaxValue || i + 8 + size > d.Length) break;
            if (i + total > d.Length) total = d.Length - i; // missing final pad byte

            if (Ascii(d, i, "EXIF") || Ascii(d, i, "XMP "))
            {
                removed = true;
            }
            else
            {
                if (Ascii(d, i, "VP8X") && size >= 1) vp8xFlagsOffset = (int)output.Position + 8;
                output.Write(d, i, (int)total);
            }
            i += (int)total;
        }
        if (!removed) return d;
        if (i < d.Length) output.Write(d, i, d.Length - i);

        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)(result.Length - 8));
        if (vp8xFlagsOffset >= 0) result[vp8xFlagsOffset] &= unchecked((byte)~(VP8XExifFlag | VP8XXmpFlag));
        return result;
    }

    private static bool Ascii(byte[] d, int offset, string text)
    {
        if (offset + text.Length > d.Length) return false;
        for (var k = 0; k < text.Length; k++)
            if (d[offset + k] != (byte)text[k]) return false;
        return true;
    }
}
