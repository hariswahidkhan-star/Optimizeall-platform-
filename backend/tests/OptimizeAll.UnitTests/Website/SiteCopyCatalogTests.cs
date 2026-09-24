using OptimizeAll.Api.Modules.Content.Copy;
using OptimizeAll.Api.Modules.Website.Shared;

namespace OptimizeAll.UnitTests.Website;

/// <summary>
/// The editable page copy catalog: the backend's embedded <c>site-copy.json</c> must be identical to the web app's
/// fallback copy (<c>frontend/src/features/public/site/siteCopy.json</c>), so the defaults an editor resets to are exactly
/// what visitors see before any override loads.
/// </summary>
public sealed class SiteCopyCatalogTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray())))
            dir = dir.Parent;
        Assert.True(dir is not null, $"{string.Join('/', parts)} was not found above the test directory.");
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    [Fact]
    public void Backend_and_frontend_catalogs_are_identical()
    {
        var backend = File.ReadAllText(RepoFile("backend", "src", "OptimizeAll.Api", "Modules", "Content", "Copy", "site-copy.json"));
        var frontend = File.ReadAllText(RepoFile("frontend", "src", "features", "public", "site", "siteCopy.json"));
        Assert.True(backend.ReplaceLineEndings() == frontend.ReplaceLineEndings(),
            "site-copy.json and siteCopy.json differ: copy the edited file over the other one.");
    }

    [Fact]
    public void Embedded_catalog_loads_with_unique_keys_and_valid_defaults()
    {
        var groups = SiteCopyCatalog.Groups;
        Assert.NotEmpty(groups);
        Assert.Contains(groups, g => g.Scope == CopyScope.Website);
        Assert.Contains(groups, g => g.Scope == CopyScope.Portal);
        var keys = groups.SelectMany(g => g.Entries).Select(e => e.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(keys.Count, SiteCopyCatalog.ByKey.Count);

        // Every shipped default passes the same validation an editor's text does (so "reset" is always valid).
        foreach (var entry in groups.SelectMany(g => g.Entries))
        {
            var errors = new FieldErrors();
            var cleaned = SiteCopyService.Validate(entry, entry.Default, entry.Key, errors);
            Assert.False(errors.Any, $"Default of {entry.Key} is invalid.");
            Assert.Equal(entry.Default, cleaned);
        }
    }

    [Theory]
    [InlineData("home.hero.title", "One\nTwo", "Use a single line.")]
    [InlineData("home.process.steps", "Audit without a separator", "Title | Text")]
    [InlineData("shared.footer.copyright", "© {yeer} Us", "Unknown placeholder {yeer}")]
    [InlineData("home.hero.eyebrow", "   ", "Enter the text")]
    public void Invalid_values_are_rejected(string key, string value, string expected)
    {
        var errors = new FieldErrors();
        SiteCopyService.Validate(SiteCopyCatalog.ByKey[key], value, key, errors);
        var ex = Assert.Throws<OptimizeAll.Domain.Common.DomainException>(() => errors.ThrowIfAny());
        Assert.Contains(ex.Errors![key], m => m.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Markup_and_control_characters_are_stripped()
    {
        var errors = new FieldErrors();
        var value = SiteCopyService.Validate(SiteCopyCatalog.ByKey["home.hero.lead"], "Grow <script>alert(1)</script>fast\u0007\r\nnow", "k", errors);
        Assert.False(errors.Any);
        Assert.Equal("Grow alert(1)fast\nnow", value);
    }
}
