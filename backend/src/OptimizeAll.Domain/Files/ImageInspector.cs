using System.Buffers.Binary;

namespace OptimizeAll.Domain.Files;

public sealed record ImageInfo(string ContentType, string Extension, int Width, int Height);

/// <summary>
/// Identifies PNG, JPEG and WebP images from their magic bytes and reads the pixel dimensions from the image
/// header. The client's Content-Type and file name are never trusted. Returns null for anything else
/// (including other formats renamed to .png and truncated/malformed headers).
/// </summary>
public static class ImageInspector
{
    private static ReadOnlySpan<byte> PngSignature => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static ImageInfo? Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[..8].SequenceEqual(PngSignature)) return Png(data);
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return Jpeg(data);
        if (data.Length >= 12 && Ascii(data, 0, "RIFF") && Ascii(data, 8, "WEBP")) return WebP(data);
        return null;
    }

    private static ImageInfo? Png(ReadOnlySpan<byte> d)
    {
        // Signature, then the IHDR chunk must come first: length(4) "IHDR" width(4) height(4).
        if (d.Length < 24 || !Ascii(d, 12, "IHDR")) return null;
        var width = BinaryPrimitives.ReadUInt32BigEndian(d.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(d.Slice(20, 4));
        return Build("image/png", ".png", width, height);
    }

    private static ImageInfo? Jpeg(ReadOnlySpan<byte> d)
    {
        var i = 2;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF) return null;
            var marker = d[i + 1];
            if (marker == 0xFF) { i++; continue; } // fill byte
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; } // no length
            if (marker is 0xD9 or 0xDA) return null; // end of image / start of scan before a frame header

            var length = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 2, 2));
            if (length < 2) return null;
            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isStartOfFrame)
            {
                if (i + 9 > d.Length || length < 7) return null;
                var height = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 7, 2));
                return Build("image/jpeg", ".jpg", width, height);
            }
            i += 2 + length;
        }
        return null;
    }

    private static ImageInfo? WebP(ReadOnlySpan<byte> d)
    {
        if (d.Length < 30) return null;
        if (Ascii(d, 12, "VP8X"))
        {
            // flags(1) reserved(3) canvasWidth-1 (24-bit LE) canvasHeight-1 (24-bit LE)
            var width = 1u + (uint)(d[24] | d[25] << 8 | d[26] << 16);
            var height = 1u + (uint)(d[27] | d[28] << 8 | d[29] << 16);
            return Build("image/webp", ".webp", width, height);
        }
        if (Ascii(d, 12, "VP8 "))
        {
            // frame tag(3) start code 9D 01 2A, then 14-bit width and height (LE).
            if (d[23] != 0x9D || d[24] != 0x01 || d[25] != 0x2A) return null;
            var width = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(26, 2)) & 0x3FFFu;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(28, 2)) & 0x3FFFu;
            return Build("image/webp", ".webp", width, height);
        }
        if (Ascii(d, 12, "VP8L"))
        {
            // signature 0x2F, then width-1 (14 bits) and height-1 (14 bits), little-endian bit order.
            if (d[20] != 0x2F || d.Length < 25) return null;
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(21, 4));
            var width = (bits & 0x3FFFu) + 1;
            var height = ((bits >> 14) & 0x3FFFu) + 1;
            return Build("image/webp", ".webp", width, height);
        }
        return null;
    }

    private static ImageInfo? Build(string contentType, string extension, uint width, uint height) =>
        width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue
            ? null
            : new ImageInfo(contentType, extension, (int)width, (int)height);

    private static bool Ascii(ReadOnlySpan<byte> d, int offset, string text)
    {
        if (offset + text.Length > d.Length) return false;
        for (var i = 0; i < text.Length; i++)
            if (d[offset + i] != (byte)text[i]) return false;
        return true;
    }
}
