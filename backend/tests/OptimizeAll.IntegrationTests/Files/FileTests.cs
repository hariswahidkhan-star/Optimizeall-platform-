using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Campaigns;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Files;

public sealed class FileTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static MultipartFormDataContent Upload(byte[] bytes, string name = "banner.png", string contentType = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    [Fact]
    public async Task Managers_upload_public_images_readable_anonymously()
    {
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var png = CampaignTestKit.Png(1200, 628);
        var response = await manager.PostAsync("/api/v1/admin/files", Upload(png, "../../evil name.png", "text/html"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = await response.ReadJsonAsync();
        Assert.Equal("image/png", file.GetProperty("contentType").GetString());
        Assert.Equal(1200, file.GetProperty("width").GetInt32());
        Assert.Equal(628, file.GetProperty("height").GetInt32());
        Assert.Equal("evil name.png", file.GetProperty("originalFileName").GetString());
        Assert.Equal(64, file.GetProperty("sha256").GetString()!.Length);

        var anonymous = await api.CreateClient().GetAsync(file.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal("image/png", anonymous.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await anonymous.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", anonymous.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; sandbox", anonymous.Headers.GetValues("Content-Security-Policy").Single());

        // Stored under a server-generated key inside the configured root, never the client's name.
        var stored = Directory.GetFiles(api.StorageDirectory, "*", SearchOption.AllDirectories);
        Assert.Contains(stored, f => f.EndsWith(".png") && !f.Contains("evil"));
    }

    [Theory]
    [InlineData("%PDF-1.7\n%âãÏÓ\n1 0 obj", "document.png")]
    [InlineData("<html><body><script>alert(document.cookie)</script></body></html>", "page.png")]
    [InlineData("GIF89a\u0001\u0000\u0001\u0000", "anim.png")]
    public async Task Non_images_are_rejected_whatever_their_name(string content, string name)
    {
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var bytes = System.Text.Encoding.UTF8.GetBytes(content + new string(' ', 300));
        await (await manager.PostAsync("/api/v1/admin/files", Upload(bytes, name))).ShouldFailAsync(400, "file.unsupported_type");
    }

    [Fact]
    public async Task Oversized_and_out_of_range_images_are_rejected()
    {
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var big = new byte[10 * 1024 * 1024 + 1024];
        CampaignTestKit.Png().CopyTo(big, 0);
        await (await manager.PostAsync("/api/v1/admin/files", Upload(big))).ShouldFailAsync(400, "file.too_large");
        await (await manager.PostAsync("/api/v1/admin/files", Upload(CampaignTestKit.Png(199, 400)))).ShouldFailAsync(400, "file.too_small");
        await (await manager.PostAsync("/api/v1/admin/files", Upload(CampaignTestKit.Png(10001, 400)))).ShouldFailAsync(400, "file.too_large_dimensions");
    }

    [Fact]
    public async Task Only_campaign_or_content_managers_can_upload()
    {
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PostAsync("/api/v1/admin/files", Upload(CampaignTestKit.Png()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsync("/api/v1/admin/files", Upload(CampaignTestKit.Png()))).StatusCode);
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync("/api/v1/admin/files", Upload(CampaignTestKit.Png()))).StatusCode);
    }

    [Fact]
    public async Task Uploaded_file_can_back_a_campaign_asset()
    {
        var kit = new CampaignTestKit(api);
        var (_, manager) = await kit.ManagerAsync();
        var file = await (await manager.PostAsync("/api/v1/admin/files", Upload(CampaignTestKit.Png()))).ReadJsonAsync();
        var campaign = await kit.CreateCampaignAsync(manager, publish: false);
        var asset = await (await manager.PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/assets",
            System.Net.Http.Json.JsonContent.Create(new { type = "Image", title = "Hero", fileId = file.GetProperty("id").GetGuid(), platform = "Instagram" })))
            .ReadJsonAsync();
        Assert.Equal(file.GetProperty("url").GetString(), asset.GetProperty("url").GetString());
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/assets",
            System.Net.Http.Json.JsonContent.Create(new { type = "Image", title = "Bad", url = "javascript:alert(1)" })))
            .ShouldFailAsync(400, "campaign.asset_url_invalid");
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/assets",
            System.Net.Http.Json.JsonContent.Create(new { type = "Image", title = "Wrong platform", url = file.GetProperty("url").GetString(), platform = "YouTube" })))
            .ShouldFailAsync(400, "campaign.asset_platform_invalid");
        // Image assets must be uploads (or on an allowed image host, none configured here); other asset types may
        // link to any https URL.
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/assets",
            System.Net.Http.Json.JsonContent.Create(new { type = "Image", title = "External", url = "https://placehold.co/600x600/png" })))
            .ShouldFailAsync(400, "campaign.asset_url_invalid");
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/assets",
            System.Net.Http.Json.JsonContent.Create(new { type = "Link", title = "Product page", url = "https://brand.example/product" }))).StatusCode);
    }

    [Fact]
    public async Task Image_metadata_is_stripped_and_duplicates_still_match()
    {
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var clean = CraftedPng(withGps: false);
        var tagged = CraftedPng(withGps: true);
        Assert.True(Contains(tagged, GpsMarker));

        var file = await (await manager.PostAsync("/api/v1/admin/files", Upload(tagged))).ReadJsonAsync();
        var served = await (await api.CreateClient().GetAsync(file.GetProperty("url").GetString())).Content.ReadAsByteArrayAsync();
        Assert.False(Contains(served, GpsMarker));
        Assert.False(Contains(served, "eXIf"));
        Assert.False(Contains(served, "tEXt"));
        Assert.Equal(clean, served);
        Assert.Equal(clean.Length, file.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(clean)).ToLowerInvariant(),
            file.GetProperty("sha256").GetString()!.ToLowerInvariant());

        // The same picture uploaded as screenshots (with and without metadata) is still flagged as a duplicate.
        var kit = new CampaignTestKit(api);
        var (_, creator) = await kit.ManagerAsync();
        var campaign = await kit.CreateCampaignAsync(creator, kit.CampaignBody());
        var a = await kit.ParticipantAsync();
        var b = await kit.ParticipantAsync();
        await kit.SubmitOkAsync(a, campaign.Id, screenshot: tagged);
        var second = await kit.SubmitOkAsync(b, campaign.Id, screenshot: clean);
        var flags = await api.WithDbAsync(db => db.Set<OptimizeAll.Domain.Submissions.SubmissionFlag>()
            .Where(f => f.SubmissionId == second).Select(f => f.Type).ToListAsync());
        Assert.Contains(OptimizeAll.Domain.Submissions.SubmissionFlagType.DuplicateScreenshot, flags);
    }

    private const string GpsMarker = "GPSLatitude=24.8607N;GPSLongitude=67.0011E";

    private static bool Contains(byte[] data, string text) => data.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(text)) >= 0;

    /// <summary>A structurally valid PNG (random pixel payload) optionally carrying GPS EXIF and text chunks.</summary>
    private static byte[] CraftedPng(bool withGps)
    {
        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
            s.Write(len);
            s.Write(System.Text.Encoding.ASCII.GetBytes(type));
            s.Write(data);
            s.Write(new byte[4]); // CRC is not validated
        }

        var s = new MemoryStream();
        s.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr, 800);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 600);
        ihdr[8] = 8; ihdr[9] = 2;
        Chunk(s, "IHDR", ihdr);
        if (withGps)
        {
            Chunk(s, "eXIf", System.Text.Encoding.ASCII.GetBytes("II*\0" + GpsMarker));
            Chunk(s, "tEXt", System.Text.Encoding.ASCII.GetBytes("Location\0" + GpsMarker));
        }
        Chunk(s, "IDAT", Payload);
        Chunk(s, "IEND", Array.Empty<byte>());
        return s.ToArray();
    }

    private static readonly byte[] Payload = System.Security.Cryptography.RandomNumberGenerator.GetBytes(256);
}
