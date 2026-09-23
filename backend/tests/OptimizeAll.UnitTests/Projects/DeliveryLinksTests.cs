using System.Reflection;
using System.Text.Json;
using OptimizeAll.Api.Modules.Projects;

namespace OptimizeAll.UnitTests.Projects;

/// <summary>
/// Keeps <see cref="DeliveryLinks"/> in sync with <c>frontend/src/features/agency/shared/deliveryLinks.fixture.json</c>,
/// whose paths the frontend test <c>deliveryLinks.test.ts</c> resolves against the real router.
/// </summary>
public sealed class DeliveryLinksTests
{
    private static Dictionary<string, string> BackendPatterns()
    {
        var result = new Dictionary<string, string>();
        foreach (var field in typeof(DeliveryLinks).GetFields(BindingFlags.Public | BindingFlags.Static))
            result[field.Name] = (string)field.GetValue(null)!;
        foreach (var method in typeof(DeliveryLinks).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var parameters = method.GetParameters();
            var samples = parameters.Select(_ => Guid.NewGuid()).ToArray();
            var path = (string)method.Invoke(null, samples.Cast<object>().ToArray())!;
            for (var i = 0; i < parameters.Length; i++) path = path.Replace(samples[i].ToString(), ":" + parameters[i].Name);
            result[method.Name] = path;
        }
        return result;
    }

    private static Dictionary<string, string> FixturePatterns()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        static string PathIn(string root) => Path.Combine(root, "frontend", "src", "features", "agency", "shared", "deliveryLinks.fixture.json");
        while (dir is not null && !File.Exists(PathIn(dir.FullName))) dir = dir.Parent;
        Assert.True(dir is not null, "deliveryLinks.fixture.json was not found above the test directory.");
        using var json = JsonDocument.Parse(File.ReadAllText(PathIn(dir!.FullName)));
        return json.RootElement.GetProperty("links").EnumerateArray()
            .ToDictionary(l => l.GetProperty("name").GetString()!, l => l.GetProperty("path").GetString()!);
    }

    [Fact]
    public void Backend_links_and_frontend_fixture_are_identical() =>
        Assert.Equal(BackendPatterns().OrderBy(k => k.Key), FixturePatterns().OrderBy(k => k.Key));
}
