using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing.Patterns;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>One request of the matrix and what its answer must satisfy besides the general response contract.</summary>
public sealed record Case(string Name, Func<HttpRequestMessage> Build, Func<Outcome, string?>? Expect = null);

/// <summary>
/// The hostile-input matrix of an endpoint, generated from its metadata: malformed/oversized route and query values, JSON
/// bodies that are empty, malformed, of the wrong shape or with every property of a kind replaced by a hostile value (wrong
/// types, nulls, 10k/1M-char strings, negative/zero/max/overflowing numbers, undefined enum names and numbers, out-of-range
/// and invalid dates/GUIDs, lists with null elements), unsupported media types and broken multipart uploads.
/// </summary>
public static class HostileCases
{
    public static readonly string Str10K = new('x', 10_000);
    public static readonly string Str1M = new('y', 1_000_000);
    private const string Weird = "\u0000💥'\"%_\\<script>alert(1)</script>";
    private const string WeirdInPath = "💥'\"%_\\<script>alert(1)</script>"; // HTTP paths cannot carry NUL
    private static readonly byte[] Oversize = new byte[13 * 1024 * 1024];

    private static string? Expect4xx(Outcome o) => o.Status is >= 400 and < 500 ? null : $"expected 4xx, got {o.Short}";
    private static string? Expect400(Outcome o) => o.Status == 400 ? null : $"expected 400, got {o.Short}";
    private static string? ExpectNot2xx(Outcome o) => o.IsSuccess ? $"expected a rejection, got {o.Short}" : null;
    private static string? Expect404(Outcome o) => o.Status == 404 ? null : $"expected 404, got {o.Short}";

    public static HttpMethod MethodOf(ApiEndpoint e) => new(e.Method == "*" ? "GET" : e.Method);

    public static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>The default body of an endpoint (a full sample of its DTO), or null when it binds none.</summary>
    public static JsonNode? BaseBody(ApiEndpoint e, bool minimal = false) =>
        e.Body is null ? null : Samples.Value(e.Body.Type, e.Body.Name, 0, minimal) ?? new JsonObject();

    public static MultipartFormDataContent BaseForm(ApiEndpoint e, byte[]? file = null, string fileName = "sample.txt", string fileType = "text/plain")
    {
        var content = new MultipartFormDataContent();
        foreach (var p in e.Form)
        {
            if (Samples.KindOf(p.Type) == ValueKind.File)
            {
                var part = new ByteArrayContent(file ?? Encoding.UTF8.GetBytes("hello contract"));
                part.Headers.ContentType = new MediaTypeHeaderValue(fileType);
                content.Add(part, p.Name, fileName);
            }
            else
            {
                content.Add(new StringContent(Samples.Scalar(p.Type, p.Name)), p.Name);
            }
        }
        return content;
    }

    /// <summary>The ordinary request of an endpoint: fresh ids in the route, required query values and a sample body/form.</summary>
    public static HttpRequestMessage BaseRequest(ApiEndpoint e, Func<string, string>? guid = null, IReadOnlyDictionary<string, string>? route = null,
        IEnumerable<(string, string)>? query = null)
    {
        var request = new HttpRequestMessage(MethodOf(e), Samples.Path(e, guid, route) + Samples.Query(query ?? Samples.BaseQuery(e)));
        if (e.HasBody) request.Content = JsonContent(BaseBody(e)!.ToJsonString());
        else if (e.HasForm) request.Content = BaseForm(e);
        else if (e.Method is "POST" or "PUT" or "PATCH") request.Content = JsonContent("{}");
        return request;
    }

    /// <summary>
    /// The matrix of <paramref name="e"/>. With <paramref name="realRoute"/> (ids of existing records), only the base and body/form
    /// cases, sent to those records so the handler logic behind the lookup sees the hostile values too.
    /// </summary>
    public static IEnumerable<Case> For(ApiEndpoint e, IReadOnlyDictionary<string, string>? realRoute = null)
    {
        var anonymous = e.AllowAnonymous;
        var real = realRoute is not null;
        var method = MethodOf(e);
        var path = Samples.Path(e, overrides: realRoute);
        var query = Samples.BaseQuery(e).ToList();
        var qs = Samples.Query(query);

        HttpRequestMessage Req(string p, string q, HttpContent? content = null) => new(method, p + q) { Content = content };
        HttpRequestMessage WithBody(HttpContent? content) => Req(path, qs, content);
        HttpContent? DefaultContent() => e.HasBody ? JsonContent(BaseBody(e)!.ToJsonString()) : e.HasForm ? BaseForm(e) : null;

        // ---- Base requests (unknown ids everywhere).
        // Unknown ids are never a success (404, or a 400/409 when the request is rejected before the lookup).
        var guidRoute = e.RouteParameters.Any(p => p.ParameterPolicies.Any(pp => pp.Content == "guid"));
        yield return real
            ? new Case("base", () => BaseRequest(e, route: realRoute))
            : new Case("base", () => BaseRequest(e), guidRoute ? ExpectNot2xx : null);
        if (e.HasBody) yield return new Case("base-minimal", () => WithBody(JsonContent(BaseBody(e, minimal: true)!.ToJsonString())));

        // ---- Route values.
        if (!real)
        {
        var pathTypes = e.Parameters.Where(p => p.Source == BindingSource.Path).ToDictionary(p => p.Name, p => p.Type, StringComparer.OrdinalIgnoreCase);
        foreach (var rp in e.RouteParameters)
        {
            var name = rp.Name;
            var constrained = rp.ParameterPolicies.Any(pp => pp.Content is "guid" or "int" or "long");
            var values = constrained
                ? new[] { "not-a-guid", "99999999999999999999" }
                : new[] { "not-a-guid", "999", "-1", new string('z', 2000), WeirdInPath, "..", "NotAValue" };
            // A value the route constraint rejects matches no endpoint: 404 (401 for an anonymous caller: default deny).
            Func<Outcome, string?> unmatched = anonymous
                ? o => o.Status is 404 or 401 ? null : $"expected 404/401, got {o.Short}"
                : Expect404;
            foreach (var v in values)
                yield return new Case($"route {name}={Trim(v)}", () => DefaultWith(Req(Samples.Path(e, overrides: new Dictionary<string, string> { [name] = v }), qs)),
                    constrained ? unmatched : null);
            if (!constrained && pathTypes.TryGetValue(name, out var t) && Samples.KindOf(t) == ValueKind.Guid)
                yield return new Case($"route {name}=unknown", () => DefaultWith(Req(Samples.Path(e), qs)), e.HasBody ? ExpectNot2xx : Expect404);
        }
        }

        HttpRequestMessage DefaultWith(HttpRequestMessage r)
        {
            r.Content = DefaultContent() ?? (e.Method is "POST" or "PUT" or "PATCH" ? JsonContent("{}") : null);
            return r;
        }

        // ---- Query values (one parameter at a time, the others at their defaults).
        foreach (var qp in real ? Enumerable.Empty<EndpointParameter>() : e.Query)
        {
            var kind = Samples.KindOf(qp.Type);
            var element = kind == ValueKind.List ? Samples.ElementType(qp.Type) : null;
            var values = (element is null ? kind : Samples.KindOf(element)) switch
            {
                ValueKind.Integer => new[] { "abc", "-1", "0", "2147483647", "2147483648", "99999999999999999999" },
                ValueKind.Float => new[] { "abc", "-1", "1e400", "79228162514264337593543950336" },
                ValueKind.Enum => new[] { "NotAValue", "999", "-1" },
                ValueKind.Guid => new[] { "not-a-guid", Guid.Empty.ToString() },
                ValueKind.Date => new[] { "2024-13-45", "0001-01-01", "9999-12-31T23:59:59Z" },
                ValueKind.DateOnly => new[] { "2024-13-45", "0001-01-01", "9999-12-31" },
                ValueKind.Bool => new[] { "maybe" },
                ValueKind.String => new[] { Str10K, Weird, "" },
                _ => new[] { "garbage" },
            };
            var others = query.Where(q => !q.Name.Equals(qp.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var v in values)
            {
                var q = Samples.Query(others.Append((qp.Name, v)));
                yield return new Case($"query {qp.Name}={Trim(v)}", () => DefaultWith(Req(path, q)));
            }
            var sample = Samples.Scalar(element ?? qp.Type, qp.Name);
            var dup = Samples.Query(others.Append((qp.Name, sample)).Append((qp.Name, sample)));
            yield return new Case($"query {qp.Name} duplicated", () => DefaultWith(Req(path, dup)));
            if (qp.Name.Equals("pageSize", StringComparison.OrdinalIgnoreCase))
            {
                var big = Samples.Query(others.Append((qp.Name, "100000")));
                yield return new Case("query pageSize=100000", () => DefaultWith(Req(path, big)), o =>
                    o.IsSuccess && o.Json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("pageSize", out var ps) &&
                    ps.ValueKind == JsonValueKind.Number && ps.GetInt32() > 1000
                        ? $"pageSize 100000 was not clamped ({ps.GetInt32()})"
                        : null);
            }
        }

        // ---- JSON bodies.
        if (e.Body is { } body)
        {
            var type = body.Type;
            var isObject = Samples.KindOf(type) == ValueKind.Object;
            var baseBody = BaseBody(e)!;

            // An optional body ([FromBody(EmptyBodyBehavior = Allow)]) may be absent, empty or null.
            var required = body.IsRequired;
            yield return new Case("body absent", () => WithBody(null), required ? Expect4xx : null);
            yield return new Case("body empty", () => WithBody(JsonContent(string.Empty)), required ? Expect400 : null);
            yield return new Case("body {}", () => WithBody(JsonContent("{}")), isObject ? null : Expect400);
            yield return new Case("body malformed", () => WithBody(JsonContent("{\"a\":")), Expect400);
            yield return new Case("body null", () => WithBody(JsonContent("null")), required ? Expect4xx : null);
            yield return new Case("body 123", () => WithBody(JsonContent("123")), Expect400);
            if (isObject) yield return new Case("body []", () => WithBody(JsonContent("[]")), Expect400);
            yield return new Case("body text/plain", () => WithBody(new StringContent("hello", Encoding.UTF8, "text/plain")), o => o.Status == 415 ? null : $"expected 415, got {o.Short}");
            yield return new Case("body charset=bogus", () =>
            {
                var c = new ByteArrayContent(Encoding.UTF8.GetBytes(baseBody.ToJsonString()));
                c.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=bogus");
                return WithBody(c);
            }, Expect4xx);
            yield return new Case("body extra fields", () =>
            {
                var b = baseBody.DeepClone();
                if (b is JsonObject o) o["zzzUnknownField"] = 1;
                return WithBody(JsonContent(b.ToJsonString()));
            });

            if (isObject)
            {
                Case Mutate(string name, Func<DtoProperty, JsonNode?, JsonNode?> map, Func<Outcome, string?>? expect = null) =>
                    new(name, () => WithBody(JsonContent((Samples.Transform(baseBody, type, map) ?? new JsonObject()).ToJsonString())), expect);

                var kinds = Samples.Leaves(type).Select(p => p.Kind).ToHashSet();
                yield return Mutate("props wrong types", (p, v) => p.Kind switch
                {
                    ValueKind.String => 12345,
                    ValueKind.Integer or ValueKind.Float => "abc",
                    ValueKind.Bool => "yes",
                    ValueKind.Enum => true,
                    ValueKind.Guid => 123,
                    ValueKind.Date or ValueKind.DateOnly or ValueKind.Time => true,
                    ValueKind.List => "not-a-list",
                    ValueKind.Dictionary => new JsonArray(1),
                    _ => v,
                }, kinds.Overlaps(new[] { ValueKind.String, ValueKind.Integer, ValueKind.Float, ValueKind.Bool, ValueKind.Enum, ValueKind.Guid,
                    ValueKind.Date, ValueKind.DateOnly, ValueKind.List }) ? Expect400 : null);
                yield return Mutate("props null", (_, _) => null);
                if (kinds.Contains(ValueKind.String))
                {
                    yield return Mutate("strings empty", (p, v) => p.Kind == ValueKind.String ? "" : v);
                    yield return Mutate("strings whitespace", (p, v) => p.Kind == ValueKind.String ? "   " : v);
                    yield return Mutate("strings weird", (p, v) => p.Kind == ValueKind.String ? Weird : v);
                    yield return Mutate("strings 10k", (p, v) => p.Kind == ValueKind.String ? Str10K : v);
                    yield return Mutate("strings 1M", (p, v) => p.Kind == ValueKind.String ? Str1M : v, o => o.IsSuccess ? $"[length] 1M-char strings accepted: {o.Short}" : null);
                }
                if (kinds.Contains(ValueKind.Integer) || kinds.Contains(ValueKind.Float))
                {
                    yield return Mutate("numbers -1", (p, v) => p.Kind is ValueKind.Integer or ValueKind.Float ? -1 : v);
                    yield return Mutate("numbers 0", (p, v) => p.Kind is ValueKind.Integer or ValueKind.Float ? 0 : v);
                    yield return Mutate("numbers max", (p, v) => p.Kind switch
                    {
                        ValueKind.Integer => MaxOf(p.Underlying),
                        ValueKind.Float => JsonNode.Parse(p.Underlying == typeof(decimal) ? "79228162514264337593543950335" : "1e300"),
                        _ => v,
                    });
                    yield return Mutate("numbers overflow", (p, v) => p.Kind is ValueKind.Integer or ValueKind.Float ? JsonNode.Parse("1e400") : v, Expect400);
                }
                if (kinds.Contains(ValueKind.Enum))
                {
                    yield return Mutate("enums unknown name", (p, v) => p.Kind == ValueKind.Enum ? "NotAValue" : v, Expect400);
                    yield return Mutate("enums undefined number", (p, v) => p.Kind == ValueKind.Enum ? 999 : v, Expect400);
                }
                if (kinds.Contains(ValueKind.Date) || kinds.Contains(ValueKind.DateOnly))
                {
                    yield return Mutate("dates min", (p, v) => p.Kind switch
                    {
                        ValueKind.Date => "0001-01-01T00:00:00Z", ValueKind.DateOnly => "0001-01-01", _ => v,
                    });
                    yield return Mutate("dates max", (p, v) => p.Kind switch
                    {
                        ValueKind.Date => "9999-12-31T23:59:59Z", ValueKind.DateOnly => "9999-12-31", _ => v,
                    });
                    yield return Mutate("dates invalid", (p, v) => p.Kind is ValueKind.Date or ValueKind.DateOnly ? "2024-13-45" : v, Expect400);
                }
                if (kinds.Contains(ValueKind.Guid))
                {
                    yield return Mutate("guids empty", (p, v) => p.Kind == ValueKind.Guid ? Guid.Empty.ToString() : v);
                    yield return Mutate("guids invalid", (p, v) => p.Kind == ValueKind.Guid ? "not-a-guid" : v, Expect400);
                }
                if (kinds.Contains(ValueKind.List))
                {
                    yield return Mutate("lists [null]", (p, v) => p.Kind == ValueKind.List ? new JsonArray((JsonNode?)null) : v);
                    yield return Mutate("lists 2000 items", (p, v) =>
                    {
                        if (p.Kind != ValueKind.List || Samples.ElementType(p.Type) is not { } el || Samples.KindOf(el) == ValueKind.Object) return v;
                        var arr = new JsonArray();
                        for (var i = 0; i < 2000; i++) arr.Add(Samples.Value(el, p.JsonName));
                        return arr;
                    });
                }
                if (kinds.Contains(ValueKind.Object))
                    yield return Mutate("objects {}", (p, v) => p.Kind == ValueKind.Object ? new JsonObject() : v);
            }
        }

        // ---- Multipart uploads.
        if (e.HasForm)
        {
            var hasFile = e.Form.Any(p => Samples.KindOf(p.Type) == ValueKind.File);
            yield return new Case("form base", () => WithBody(BaseForm(e)));
            yield return new Case("form json", () => WithBody(JsonContent("{}")), Expect4xx);
            yield return new Case("form no boundary", () =>
            {
                var c = new ByteArrayContent(Encoding.UTF8.GetBytes("garbage"));
                c.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data");
                return WithBody(c);
            }, Expect4xx);
            yield return new Case("form truncated", () =>
            {
                var c = new ByteArrayContent(Encoding.UTF8.GetBytes("--b1\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.png\"\r\nContent-Type: image/png\r\n\r\nabc"));
                c.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=b1");
                return WithBody(c);
            }, Expect4xx);
            yield return new Case("form empty", () => WithBody(new MultipartFormDataContent()), hasFile ? Expect4xx : null);
            if (hasFile)
            {
                // Public forms without a form token are treated as bots and answered with a silent "thanks" (by design),
                // so only signed-in uploads must reject bad files.
                var badFile = anonymous ? null : (Func<Outcome, string?>)Expect4xx;
                yield return new Case("form empty file", () => WithBody(BaseForm(e, Array.Empty<byte>())), badFile);
                yield return new Case("form fake png", () => WithBody(BaseForm(e, Encoding.UTF8.GetBytes("not really a png"), "a.png", "image/png")), badFile);
                yield return new Case("form weird name", () => WithBody(BaseForm(e, Encoding.UTF8.GetBytes("abc"), "../../..%2F<evil>&amp;.exe", "application/x-msdownload")), badFile);
                yield return new Case("form 13MB", () => WithBody(BaseForm(e, Oversize, "big.png", "image/png")), Expect4xx);
            }
        }
    }

    private static JsonNode MaxOf(Type t) =>
        t == typeof(long) ? JsonValue.Create(long.MaxValue) :
        t == typeof(short) ? JsonValue.Create(short.MaxValue) :
        t == typeof(byte) ? JsonValue.Create(byte.MaxValue) :
        JsonValue.Create(int.MaxValue);

    private static string Trim(string v) => v.Length > 24 ? v[..12] + $"…({v.Length})" : v.Replace("\u0000", "\\0");
}
