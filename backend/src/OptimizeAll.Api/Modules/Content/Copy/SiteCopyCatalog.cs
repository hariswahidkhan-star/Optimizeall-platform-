using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptimizeAll.Api.Modules.Content.Copy;

/// <summary>Who may edit a copy group: website marketers (<c>site.manage</c>) or platform content editors (<c>content.manage</c>).</summary>
public enum CopyScope
{
    Website,
    Portal,
}

public enum CopyType
{
    /// <summary>One line, up to 300 characters.</summary>
    Text,
    /// <summary>A paragraph (line breaks allowed), up to 5,000 characters.</summary>
    Textarea,
    /// <summary>One item per line.</summary>
    List,
    /// <summary>One item per line as <c>Title | Text</c>.</summary>
    Pairs,
}

public sealed record CopyEntryDefinition(
    string Key, string Label, CopyType Type, string Default, IReadOnlyList<string> Placeholders, string GroupId, CopyScope Scope);

public sealed record CopyGroupDefinition(string Id, string Label, CopyScope Scope, IReadOnlyList<CopyEntryDefinition> Entries);

/// <summary>
/// The catalog of editable page copy: every key the public site and portals read, its type and its shipped default.
/// Loaded from the embedded <c>site-copy.json</c>, which is kept identical to the web app's fallback copy
/// (<c>frontend/src/features/public/site/siteCopy.json</c>; the unit test SiteCopyCatalogTests enforces it).
/// </summary>
public static class SiteCopyCatalog
{
    public const string ResourceName = "OptimizeAll.SiteCopy.json";

    private static readonly Lazy<IReadOnlyList<CopyGroupDefinition>> LazyGroups = new(Load);

    public static IReadOnlyList<CopyGroupDefinition> Groups => LazyGroups.Value;

    private static readonly Lazy<IReadOnlyDictionary<string, CopyEntryDefinition>> LazyByKey =
        new(() => Groups.SelectMany(g => g.Entries).ToDictionary(e => e.Key, StringComparer.Ordinal));

    public static IReadOnlyDictionary<string, CopyEntryDefinition> ByKey => LazyByKey.Value;

    public static int MaxLength(CopyType type) => type switch
    {
        CopyType.Text => 300,
        CopyType.Textarea => 5000,
        _ => 10000,
    };

    /// <summary>Parses the catalog document (also used by the unit test against the web app's copy).</summary>
    public static IReadOnlyList<CopyGroupDefinition> Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<CatalogDocument>(json, Options)
                  ?? throw new InvalidOperationException("The site copy catalog is empty.");
        var groups = new List<CopyGroupDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in doc.Groups ?? new List<CatalogGroup>())
        {
            var entries = new List<CopyEntryDefinition>();
            foreach (var e in g.Entries ?? new List<CatalogEntry>())
            {
                if (string.IsNullOrWhiteSpace(e.Key) || !seen.Add(e.Key))
                    throw new InvalidOperationException($"Duplicate or empty site copy key '{e.Key}'.");
                entries.Add(new CopyEntryDefinition(e.Key, e.Label ?? e.Key, e.Type, e.Default ?? string.Empty,
                    e.Placeholders ?? new List<string>(), g.Id ?? string.Empty, g.Scope));
            }
            groups.Add(new CopyGroupDefinition(g.Id ?? string.Empty, g.Label ?? g.Id ?? string.Empty, g.Scope, entries));
        }
        return groups;
    }

    private static IReadOnlyList<CopyGroupDefinition> Load()
    {
        using var stream = typeof(SiteCopyCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed class CatalogDocument
    {
        public List<CatalogGroup>? Groups { get; set; }
    }

    private sealed class CatalogGroup
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public CopyScope Scope { get; set; }
        public List<CatalogEntry>? Entries { get; set; }
    }

    private sealed class CatalogEntry
    {
        public string Key { get; set; } = string.Empty;
        public string? Label { get; set; }
        public CopyType Type { get; set; }
        public string? Default { get; set; }
        public List<string>? Placeholders { get; set; }
    }
}
