using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.EmailMarketing;

/// <summary>
/// Allow-list sanitizer for the rich text of email text blocks. Output is rebuilt from scratch: only the listed tags
/// survive, every attribute is dropped except a validated <c>href</c> on links, the content of dangerous elements
/// (script, style, iframe, …) is removed, stray <c>&lt;</c>/<c>&gt;</c> are encoded and every opened tag is closed.
/// </summary>
public static partial class HtmlSanitizer
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "b", "em", "i", "u", "s", "a", "ul", "ol", "li", "h1", "h2", "h3", "h4", "blockquote", "span", "sup", "sub",
    };

    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase) { "br" };

    /// <summary>Elements removed together with their content.</summary>
    private static readonly HashSet<string> DropWithContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "noscript", "template", "svg", "math", "textarea", "select", "title", "head", "xmp", "noembed", "noframes",
    };

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var input = CommentRegex().Replace(html, string.Empty);
        var output = new StringBuilder(input.Length);
        var open = new Stack<string>();
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (c == '<')
            {
                var match = TagRegex().Match(input, i);
                if (match.Success && match.Index == i)
                {
                    var closing = match.Groups["close"].Success && match.Groups["close"].Value == "/";
                    var name = match.Groups["name"].Value.ToLowerInvariant();
                    var attributes = match.Groups["attrs"].Value;
                    i += match.Length;

                    if (DropWithContent.Contains(name))
                    {
                        if (!closing)
                        {
                            var end = input.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                            if (end < 0) { i = input.Length; continue; }
                            var gt = input.IndexOf('>', end);
                            i = gt < 0 ? input.Length : gt + 1;
                        }
                        continue;
                    }
                    if (!Allowed.Contains(name)) continue;

                    if (closing)
                    {
                        if (open.Contains(name))
                        {
                            while (open.Count > 0)
                            {
                                var top = open.Pop();
                                output.Append("</").Append(top).Append('>');
                                if (top == name) break;
                            }
                        }
                        continue;
                    }

                    if (Void.Contains(name)) { output.Append("<br>"); continue; }
                    if (name == "a")
                    {
                        var href = SafeHref(ExtractHref(attributes));
                        output.Append(href is null ? "<a>" : $"<a href=\"{WebUtility.HtmlEncode(href)}\">");
                    }
                    else output.Append('<').Append(name).Append('>');
                    open.Push(name);
                    continue;
                }
                output.Append("&lt;");
                i++;
                continue;
            }
            if (c == '>') { output.Append("&gt;"); i++; continue; }
            if (c == '&')
            {
                var entity = EntityRegex().Match(input, i);
                if (entity.Success && entity.Index == i) { output.Append(entity.Value); i += entity.Length; continue; }
                output.Append("&amp;");
                i++;
                continue;
            }
            output.Append(c);
            i++;
        }
        while (open.Count > 0) output.Append("</").Append(open.Pop()).Append('>');
        return output.ToString();
    }

    /// <summary>Plain text from sanitized HTML: block ends become new lines, links become "text (url)".</summary>
    public static string ToText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var text = LinkRegex().Replace(html, m =>
        {
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value);
            var label = StripTags(m.Groups["text"].Value);
            return string.IsNullOrWhiteSpace(href) || WebUtility.HtmlDecode(label).Trim() == href ? label : $"{label} ({href})";
        });
        text = BlockEndRegex().Replace(text, "\n");
        text = ListItemRegex().Replace(text, "\n- ");
        text = WebUtility.HtmlDecode(StripTags(text));
        text = MultiNewlineRegex().Replace(text, "\n\n");
        return text.Trim();
    }

    /// <summary>
    /// Accepts absolute http(s)/mailto/tel URLs and merge-tag URLs (e.g. <c>{{unsubscribe_url}}</c> or
    /// <c>https://shop.example/?e={{email}}</c>); everything else (javascript:, data:, relative, protocol-relative) is dropped.
    /// </summary>
    public static string? SafeHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        var value = WebUtility.HtmlDecode(href).Trim();
        if (value.Any(char.IsControl)) return null;
        if (MergeOnlyUrlRegex().IsMatch(value) && MergeTags.SystemUrlTags.Contains(MergeTags.Names(value).First())) return value;
        if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            return value.Length <= 500 ? value : null;
        return IsHttpUrl(value) ? value : null;
    }

    /// <summary>Absolute http(s) URL without credentials. Merge tags are allowed after the host.</summary>
    public static bool IsHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000) return false;
        var probe = MergeTagProbeRegex().Replace(value, "x");
        return Uri.TryCreate(probe, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) &&
               // The host itself must not come from a merge tag.
               !HostHasTagRegex().IsMatch(value);
    }

    private static string? ExtractHref(string attributes)
    {
        var m = HrefRegex().Match(attributes);
        if (!m.Success) return null;
        return m.Groups["dq"].Success ? m.Groups["dq"].Value : m.Groups["sq"].Success ? m.Groups["sq"].Value : m.Groups["uq"].Value;
    }

    private static string StripTags(string html) => AnyTagRegex().Replace(html, string.Empty);

    [GeneratedRegex(@"<!--.*?(-->|$)", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<(?<close>/)?(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>(?:\s+[^\s/>=]+(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s""'>]+))?)*)\s*/?>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"&(#[0-9]{1,7}|#x[0-9a-fA-F]{1,6}|[a-zA-Z][a-zA-Z0-9]{1,31});")]
    private static partial Regex EntityRegex();

    [GeneratedRegex(@"\bhref\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<uq>[^\s""'>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();

    [GeneratedRegex(@"^\{\{\s*[a-z_]+\s*\}\}$")]
    private static partial Regex MergeOnlyUrlRegex();

    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex MergeTagProbeRegex();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.-]*://[^/?#]*\{\{")]
    private static partial Regex HostHasTagRegex();

    [GeneratedRegex(@"<a\b[^>]*?href=""(?<href>[^""]*)""[^>]*>(?<text>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<br\s*/?>|</(p|h[1-6]|blockquote|li|ul|ol|div|tr)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"\n\s*\n(\s*\n)+")]
    private static partial Regex MultiNewlineRegex();
}
