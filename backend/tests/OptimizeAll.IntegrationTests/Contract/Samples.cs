using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing.Patterns;

namespace OptimizeAll.IntegrationTests.ApiContract;

public enum ValueKind { String, Integer, Float, Bool, Enum, Guid, Date, DateOnly, Time, List, Dictionary, Object, Json, File, Other }

/// <summary>A JSON property of a request DTO, as System.Text.Json binds it.</summary>
public sealed record DtoProperty(string JsonName, Type Type, bool Nullable)
{
    public Type Underlying => System.Nullable.GetUnderlyingType(Type) ?? Type;
    public ValueKind Kind => Samples.KindOf(Type);
}

/// <summary>
/// Builds requests from endpoint metadata: plausible ("sample") values for every bindable input, derived from the CLR types
/// the endpoint binds (DTO properties, route and query parameters), and helpers to walk and mutate JSON bodies.
/// </summary>
public static class Samples
{
    public static readonly DateTime Now = DateTime.UtcNow;
    private static readonly NullabilityInfoContext Nullability = new();

    public static ValueKind KindOf(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string)) return ValueKind.String;
        if (t == typeof(bool)) return ValueKind.Bool;
        if (t.IsEnum) return ValueKind.Enum;
        if (t == typeof(Guid)) return ValueKind.Guid;
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return ValueKind.Date;
        if (t == typeof(DateOnly)) return ValueKind.DateOnly;
        if (t == typeof(TimeOnly) || t == typeof(TimeSpan)) return ValueKind.Time;
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong))
            return ValueKind.Integer;
        if (t == typeof(decimal) || t == typeof(double) || t == typeof(float)) return ValueKind.Float;
        if (t == typeof(JsonElement) || t == typeof(JsonNode) || t == typeof(JsonObject) || t == typeof(object)) return ValueKind.Json;
        if (typeof(IFormFile).IsAssignableFrom(t) || typeof(IFormFileCollection).IsAssignableFrom(t)) return ValueKind.File;
        if (t.IsGenericType && t.GetInterfaces().Append(t).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                                                                   i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
            return ValueKind.Dictionary;
        if (t.IsArray || typeof(IEnumerable).IsAssignableFrom(t)) return ValueKind.List;
        if (t.IsClass || (t.IsValueType && !t.IsPrimitive)) return ValueKind.Object;
        return ValueKind.Other;
    }

    public static Type? ElementType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsArray) return t.GetElementType();
        return t.GetInterfaces().Append(t).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    public static IReadOnlyList<DtoProperty> Properties(Type type)
    {
        var result = new List<DtoProperty>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || p.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
            if (!p.CanWrite && type.GetConstructors().All(c => c.GetParameters().All(a => !string.Equals(a.Name, p.Name, StringComparison.OrdinalIgnoreCase))))
                continue; // computed, not bindable
            var name = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            bool nullable;
            try { nullable = Nullable.GetUnderlyingType(p.PropertyType) is not null || Nullability.Create(p).WriteState == NullabilityState.Nullable; }
            catch { nullable = false; }
            result.Add(new DtoProperty(name, p.PropertyType, nullable));
        }
        return result;
    }

    public static string SampleString(string name)
    {
        var n = name.ToLowerInvariant();
        // Unique, so an invitation sent by one tenant's caller never makes the same person a member of two tenants.
        if (n.Contains("email")) return $"contract-{Guid.NewGuid():N}@example.test";
        if (n.Contains("url") || n.Contains("website") || n.Contains("link") || n.Contains("href")) return "https://example.com/contract";
        if (n.Contains("slug")) return "contract-sample";
        if (n.Contains("currency")) return "USD";
        if (n.Contains("country")) return "US";
        if (n.Contains("timezone") || n == "tz") return "UTC";
        if (n.Contains("language") || n.Contains("locale")) return "en";
        if (n.Contains("colo")) return "#112233";
        if (n.Contains("phone") || n.Contains("mobile")) return "+14155550100";
        if (n.Contains("password")) return "Contract-Suite-Pass-2";
        if (n.Contains("domain") || n.Contains("host")) return "example.com";
        if (n.Contains("platform")) return "Instagram";
        return "Sample text";
    }

    /// <summary>A plausible JSON value of <paramref name="type"/> for a property named <paramref name="name"/>.</summary>
    public static JsonNode? Value(Type type, string name, int depth = 0, bool minimal = false)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        switch (KindOf(type))
        {
            case ValueKind.String: return SampleString(name);
            case ValueKind.Bool: return name.Equals("confirm", StringComparison.OrdinalIgnoreCase);
            case ValueKind.Enum: return Enum.GetNames(t).FirstOrDefault() is { } first ? JsonValue.Create(first) : null;
            case ValueKind.Guid: return Guid.NewGuid().ToString();
            case ValueKind.Date: return Now.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            case ValueKind.DateOnly: return DateOnly.FromDateTime(Now).ToString("yyyy-MM-dd");
            case ValueKind.Time: return t == typeof(TimeSpan) ? "01:00:00" : "10:00:00";
            case ValueKind.Integer: return 1;
            case ValueKind.Float: return 10;
            case ValueKind.Json: return new JsonObject();
            case ValueKind.Dictionary: return new JsonObject();
            case ValueKind.List:
            {
                var element = ElementType(type);
                if (minimal || element is null || depth > 3) return new JsonArray();
                return new JsonArray(Value(element, name, depth + 1));
            }
            case ValueKind.Object:
                return depth > 3 ? new JsonObject() : Object(t, depth + 1, minimal);
            default:
                return null;
        }
    }

    public static JsonObject Object(Type type, int depth = 0, bool minimal = false)
    {
        var obj = new JsonObject();
        foreach (var p in Properties(type))
            obj[p.JsonName] = minimal && p.Nullable ? null : Value(p.Type, p.JsonName, depth, minimal);
        return obj;
    }

    /// <summary>
    /// Rebuilds <paramref name="node"/> (a body of <paramref name="type"/>) replacing every leaf property value through
    /// <paramref name="map"/> (return the current node to keep it), recursing into nested objects and list elements.
    /// </summary>
    public static JsonNode? Transform(JsonNode? node, Type type, Func<DtoProperty, JsonNode?, JsonNode?> map, int depth = 0)
    {
        if (node is not JsonObject obj || depth > 4) return node?.DeepClone();
        var copy = new JsonObject();
        var props = Properties(Nullable.GetUnderlyingType(type) ?? type).ToDictionary(p => p.JsonName);
        foreach (var (key, value) in obj)
        {
            if (!props.TryGetValue(key, out var p)) { copy[key] = value?.DeepClone(); continue; }
            switch (p.Kind)
            {
                case ValueKind.Object when value is JsonObject:
                    copy[key] = Transform(value, p.Type, map, depth + 1);
                    break;
                case ValueKind.List when value is JsonArray array && ElementType(p.Type) is { } element && KindOf(element) == ValueKind.Object:
                    var items = new JsonArray();
                    foreach (var item in array) items.Add(Transform(item, element, map, depth + 1));
                    // Lists themselves can be mutated too (e.g. [null]).
                    copy[key] = map(p, items);
                    break;
                default:
                    copy[key] = map(p, value?.DeepClone());
                    break;
            }
        }
        return copy;
    }

    /// <summary>Whether any leaf of <paramref name="type"/> (recursively) has one of <paramref name="kinds"/>.</summary>
    public static bool Has(Type type, params ValueKind[] kinds) => Leaves(type).Any(p => kinds.Contains(p.Kind));

    public static IEnumerable<DtoProperty> Leaves(Type type, int depth = 0)
    {
        if (depth > 4) yield break;
        foreach (var p in Properties(Nullable.GetUnderlyingType(type) ?? type))
        {
            yield return p;
            if (p.Kind == ValueKind.Object)
                foreach (var inner in Leaves(p.Underlying, depth + 1)) yield return inner;
            if (p.Kind == ValueKind.List && ElementType(p.Type) is { } element && KindOf(element) == ValueKind.Object)
                foreach (var inner in Leaves(element, depth + 1)) yield return inner;
        }
    }

    /// <summary>A sample route/query value for a parameter of <paramref name="type"/> named <paramref name="name"/>.</summary>
    public static string Scalar(Type type, string name)
    {
        var node = Value(type, name);
        return node switch
        {
            null => "sample",
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v => v.ToJsonString(),
            _ => "sample",
        };
    }

    private static readonly string[] PagingNames = { "page", "pagesize", "skip", "take", "limit", "top", "desc", "sort", "search", "q" };

    /// <summary>Query parameters a request includes by default: required ones and non-nullable ids/dates (not paging).</summary>
    public static bool InBaseQuery(EndpointParameter p)
    {
        if (p.IsRequired) return true;
        if (PagingNames.Contains(p.Name.ToLowerInvariant())) return false;
        if (Nullable.GetUnderlyingType(p.Type) is not null || !p.Type.IsValueType) return false;
        return KindOf(p.Type) is ValueKind.Guid or ValueKind.Date or ValueKind.DateOnly or ValueKind.Enum;
    }

    /// <summary>
    /// The request path with route values: guid parameters get <paramref name="guid"/>(name) (a fresh id by default), other
    /// parameters a sample of their bound type, <paramref name="overrides"/> win.
    /// </summary>
    public static string Path(ApiEndpoint e, Func<string, string>? guid = null, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var types = e.Parameters.Where(p => p.Source == BindingSource.Path).ToDictionary(p => p.Name, p => p.Type, StringComparer.OrdinalIgnoreCase);
        return "/" + string.Join('/', e.Endpoint.RoutePattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart p when overrides is not null && overrides.TryGetValue(p.Name, out var value) => Uri.EscapeDataString(value),
            RoutePatternParameterPart p when RegexChoice(p) is { } choice => choice,
            RoutePatternParameterPart p when p.ParameterPolicies.Any(pp => pp.Content == "guid") => (guid ?? (_ => Guid.NewGuid().ToString()))(p.Name),
            RoutePatternParameterPart p when p.ParameterPolicies.Any(pp => pp.Content is "int" or "long") => "1",
            RoutePatternParameterPart p when types.TryGetValue(p.Name, out var t) && KindOf(t) == ValueKind.Guid =>
                (guid ?? (_ => Guid.NewGuid().ToString()))(p.Name),
            RoutePatternParameterPart p when types.TryGetValue(p.Name, out var t) => Uri.EscapeDataString(Scalar(t, p.Name)),
            RoutePatternParameterPart => "sample",
            _ => string.Empty,
        }))));
    }

    /// <summary>For a <c>{op:regex(^(a|b)$)}</c> parameter, its first alternative (so the route matches).</summary>
    public static string? RegexChoice(RoutePatternParameterPart p)
    {
        var regex = p.ParameterPolicies.Select(pp => pp.Content).FirstOrDefault(c => c?.StartsWith("regex(", StringComparison.Ordinal) == true);
        if (regex is null) return null;
        var body = regex["regex(".Length..^1].TrimStart('^').TrimEnd('$').Trim('(', ')');
        return body.Split('|')[0];
    }

    public static string Query(IEnumerable<(string Name, string Value)> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? string.Empty : "?" + string.Join('&', list.Select(v => $"{Uri.EscapeDataString(v.Name)}={Uri.EscapeDataString(v.Value)}"));
    }

    public static IEnumerable<(string Name, string Value)> BaseQuery(ApiEndpoint e) =>
        e.Query.Where(InBaseQuery).Select(p => (p.Name, Scalar(p.Type, p.Name)));
}
