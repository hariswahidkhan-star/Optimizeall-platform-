using System.Buffers.Binary;
using System.Text;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Domain.Files;

namespace OptimizeAll.UnitTests.Files;

/// <summary>Crafted images carrying GPS EXIF, XMP and text metadata.</summary>
internal static class CraftedImages
{
    /// <summary>GPS latitude/longitude as they would appear inside an EXIF GPS IFD, plus a recognisable marker.</summary>
    public const string GpsMarker = "GPS-24.8607N-67.0011E";

    /// <summary>A minimal little-endian TIFF/EXIF block: IFD0 with a GPSInfo pointer (0x8825) to a GPS IFD.</summary>
    public static byte[] ExifWithGps()
    {
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());
        tiff.AddRange(new byte[] { 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00 }); // magic 42, IFD0 at 8
        tiff.AddRange(new byte[] { 0x01, 0x00 });                           // 1 entry
        tiff.AddRange(new byte[] { 0x25, 0x88, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x1A, 0x00, 0x00, 0x00 }); // GPSInfo → 26
        tiff.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });              // no next IFD
        tiff.AddRange(new byte[] { 0x01, 0x00 });                           // GPS IFD: 1 entry
        tiff.AddRange(new byte[] { 0x01, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00, 0x00, (byte)'N', 0x00, 0x00, 0x00 }); // GPSLatitudeRef = "N"
        tiff.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        tiff.AddRange(Encoding.ASCII.GetBytes(GpsMarker));
        return tiff.ToArray();
    }

    public static byte[] Jpeg(bool withMetadata, int width = 640, int height = 480)
    {
        var s = new MemoryStream();
        s.Write(new byte[] { 0xFF, 0xD8 });
        Segment(s, 0xE0, Concat("JFIF\0"u8.ToArray(), new byte[] { 1, 1, 0, 0, 1, 0, 1, 0, 0 }));
        if (withMetadata)
        {
            Segment(s, 0xE1, Concat("Exif\0\0"u8.ToArray(), ExifWithGps()));
            Segment(s, 0xE1, Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta><exif:GPSLatitude>24,51.6N</exif:GPSLatitude></x:xmpmeta>"));
            Segment(s, 0xED, Encoding.ASCII.GetBytes("Photoshop 3.0\08BIM IPTC city=Karachi"));
            Segment(s, 0xFE, Encoding.ASCII.GetBytes("Shot at home, " + GpsMarker));
        }
        Segment(s, 0xE2, Concat("ICC_PROFILE\0"u8.ToArray(), new byte[] { 1, 1, 0xAA, 0xBB, 0xCC }));
        Segment(s, 0xDB, Concat(new byte[] { 0x00 }, Enumerable.Repeat((byte)1, 64).ToArray())); // DQT
        var sof = new byte[] { 8, 0, 0, 0, 0, 1, 1, 0x11, 0 };
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(1), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(3), (ushort)width);
        Segment(s, 0xC0, sof);
        Segment(s, 0xC4, new byte[] { 0x00, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x00 }); // DHT
        Segment(s, 0xDA, new byte[] { 1, 1, 0x00, 0, 63, 0 });                                      // SOS
        s.Write(new byte[] { 0x12, 0x34, 0xFF, 0x00, 0x56, 0xFF, 0xD0, 0x78 });                      // scan data (stuffed 0xFF00, RST0)
        s.Write(new byte[] { 0xFF, 0xD9 });
        return s.ToArray();
    }

    public static byte[] Png(bool withMetadata, int width = 640, int height = 480, byte seed = 7)
    {
        var s = new MemoryStream();
        s.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 2;
        Chunk(s, "IHDR", ihdr);
        if (withMetadata)
        {
            Chunk(s, "tEXt", Encoding.Latin1.GetBytes("Comment\0" + GpsMarker));
            Chunk(s, "eXIf", ExifWithGps());
            Chunk(s, "tIME", new byte[] { 0x07, 0xEA, 9, 23, 12, 0, 0 });
        }
        Chunk(s, "iCCP", Encoding.Latin1.GetBytes("sRGB\0\0profile"));
        Chunk(s, "IDAT", new byte[] { 0x78, 0x9C, seed, 0x00, 0x01 });
        if (withMetadata)
        {
            Chunk(s, "zTXt", Encoding.Latin1.GetBytes("Location\0\0compressed"));
            Chunk(s, "iTXt", Encoding.UTF8.GetBytes("XML:com.adobe.xmp\0\0\0\0<x:xmpmeta>" + GpsMarker + "</x:xmpmeta>"));
        }
        Chunk(s, "IEND", Array.Empty<byte>());
        return s.ToArray();
    }

    public static byte[] WebP(bool withMetadata, int width = 640, int height = 480)
    {
        var chunks = new MemoryStream();
        var vp8x = new byte[10];
        vp8x[0] = (byte)(withMetadata ? 0x20 | 0x08 | 0x04 : 0x20); // ICC + EXIF + XMP flags
        WriteUInt24(vp8x.AsSpan(4), width - 1);
        WriteUInt24(vp8x.AsSpan(7), height - 1);
        RiffChunk(chunks, "VP8X", vp8x);
        RiffChunk(chunks, "ICCP", new byte[] { 1, 2, 3 }); // odd size: padded
        RiffChunk(chunks, "VP8L", new byte[] { 0x2F, 0x7F, 0x00, 0x00, 0x00, 0x10, 0x20 });
        if (withMetadata)
        {
            RiffChunk(chunks, "EXIF", ExifWithGps());
            RiffChunk(chunks, "XMP ", Encoding.ASCII.GetBytes("<x:xmpmeta>" + GpsMarker + "</x:xmpmeta>"));
        }
        var body = chunks.ToArray();
        var s = new MemoryStream();
        s.Write("RIFF"u8);
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(4 + body.Length));
        s.Write(size);
        s.Write("WEBP"u8);
        s.Write(body);
        return s.ToArray();
    }

    private static void Segment(Stream s, byte marker, byte[] payload)
    {
        s.Write(new byte[] { 0xFF, marker });
        var len = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)(payload.Length + 2));
        s.Write(len);
        s.Write(payload);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(data);
        s.Write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }); // CRC (not checked by the stripper)
    }

    private static void RiffChunk(Stream s, string fourCc, byte[] data)
    {
        s.Write(Encoding.ASCII.GetBytes(fourCc));
        var len = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)data.Length);
        s.Write(len);
        s.Write(data);
        if (data.Length % 2 == 1) s.WriteByte(0);
    }

    private static void WriteUInt24(Span<byte> span, int value)
    {
        span[0] = (byte)value;
        span[1] = (byte)(value >> 8);
        span[2] = (byte)(value >> 16);
    }

    private static byte[] Concat(byte[] a, byte[] b) => a.Concat(b).ToArray();
}

public sealed class ImageMetadataStripperTests
{
    private static bool Contains(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    [Fact]
    public void Jpeg_drops_exif_xmp_iptc_and_comments_but_keeps_jfif_icc_and_image_data()
    {
        var original = CraftedImages.Jpeg(withMetadata: true);
        Assert.True(Contains(original, CraftedImages.GpsMarker));

        var stripped = ImageMetadataStripper.Strip(original);

        Assert.False(Contains(stripped, CraftedImages.GpsMarker));
        Assert.False(Contains(stripped, "Exif\0\0"));
        Assert.False(Contains(stripped, "xmpmeta"));
        Assert.False(Contains(stripped, "Photoshop 3.0"));
        Assert.False(Contains(stripped, new byte[] { 0xFF, 0xE1 }));
        Assert.False(Contains(stripped, new byte[] { 0xFF, 0xFE }));
        Assert.True(Contains(stripped, "JFIF\0"));
        Assert.True(Contains(stripped, "ICC_PROFILE\0"));
        // The result is exactly the metadata-free rendering of the same image (headers, tables, scan data, EOI).
        Assert.Equal(CraftedImages.Jpeg(withMetadata: false), stripped);
        Assert.Equal(new ImageInfo("image/jpeg", ".jpg", 640, 480), ImageInspector.Inspect(stripped));
    }

    [Fact]
    public void Png_drops_text_exif_and_time_chunks_but_keeps_image_chunks()
    {
        var original = CraftedImages.Png(withMetadata: true);
        var stripped = ImageMetadataStripper.Strip(original);

        foreach (var type in new[] { "eXIf", "tEXt", "zTXt", "iTXt", "tIME" })
            Assert.False(Contains(stripped, type), type);
        Assert.False(Contains(stripped, CraftedImages.GpsMarker));
        Assert.True(Contains(stripped, "iCCP"));
        Assert.Equal(CraftedImages.Png(withMetadata: false), stripped);
        Assert.Equal(new ImageInfo("image/png", ".png", 640, 480), ImageInspector.Inspect(stripped));
    }

    [Fact]
    public void WebP_drops_exif_and_xmp_chunks_and_fixes_riff_size_and_vp8x_flags()
    {
        var original = CraftedImages.WebP(withMetadata: true);
        var stripped = ImageMetadataStripper.Strip(original);

        Assert.False(Contains(stripped, "EXIF"));
        Assert.False(Contains(stripped, "XMP "));
        Assert.False(Contains(stripped, CraftedImages.GpsMarker));
        Assert.Equal((uint)(stripped.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(stripped.AsSpan(4)));
        Assert.Equal(0x20, stripped[20]); // ICC flag kept, EXIF and XMP flags cleared
        Assert.Equal(CraftedImages.WebP(withMetadata: false), stripped);
        Assert.Equal(new ImageInfo("image/webp", ".webp", 640, 480), ImageInspector.Inspect(stripped));
    }

    [Fact]
    public void Images_without_metadata_are_returned_unchanged()
    {
        foreach (var image in new[] { CraftedImages.Jpeg(false), CraftedImages.Png(false), CraftedImages.WebP(false) })
            Assert.Same(image, ImageMetadataStripper.Strip(image));
    }

    [Fact]
    public void Stripping_is_idempotent_and_identical_images_hash_identically()
    {
        var once = ImageMetadataStripper.Strip(CraftedImages.Png(true));
        Assert.Equal(once, ImageMetadataStripper.Strip(once));
        // The same picture with and without metadata ends up byte-identical (so duplicate detection still matches).
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ImageMetadataStripper.Strip(CraftedImages.Jpeg(true)))),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(CraftedImages.Jpeg(false))));
    }

    [Fact]
    public void Truncated_or_unknown_tails_are_kept_verbatim()
    {
        // A PNG whose data after IHDR is not a valid chunk sequence: nothing is dropped.
        var png = CraftedImages.Png(false);
        var truncated = png[..(png.Length - 6)];
        Assert.Same(truncated, ImageMetadataStripper.Strip(truncated));

        // Metadata before a corrupt tail is still removed and the tail is kept.
        var withMeta = CraftedImages.Png(true);
        var corrupt = withMeta.Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 1, 2 }).ToArray();
        var stripped = ImageMetadataStripper.Strip(corrupt);
        Assert.False(Contains(stripped, CraftedImages.GpsMarker));

        var notAnImage = Encoding.ASCII.GetBytes("GIF89a....");
        Assert.Same(notAnImage, ImageMetadataStripper.Strip(notAnImage));
    }
}
