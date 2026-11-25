using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MultiVRC.Core;

public sealed partial class NewsReader(LauncherPaths paths, HttpClient http)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, List<NewsItem> Items)> _cache = new();

    public async Task<List<NewsItem>> ReadAsync(NewsSource source, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(source.Url, out var cached) && DateTimeOffset.UtcNow - cached.At < Fresh) return cached.Items;
        var file = Path.Combine(paths.Root, "news-cache", $"{Hash(source.Url)}.json");
        try
        {
            var body = Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.IsFile
                ? await File.ReadAllTextAsync(uri.LocalPath, ct)
                : await http.GetStringAsync(source.Url, ct);
            var items = source.Type.Equals("json", StringComparison.OrdinalIgnoreCase) ? FromJson(body) : FromRss(body);
            items = [.. items.OrderByDescending(i => i.PublishedAt ?? DateTimeOffset.MinValue).Take(12)];
            _cache[source.Url] = (DateTimeOffset.UtcNow, items);
            JsonFiles.Write(file, items);
            return items;
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or System.Xml.XmlException or JsonException)
        {
            return JsonFiles.Read<List<NewsItem>>(file) ?? [];
        }
    }

    public static List<NewsItem> FromRss(string xml)
    {
        var doc = XDocument.Parse(xml);
        XNamespace media = "http://www.rssboard.org/media-rss";
        XNamespace mediaAlt = "http://search.yahoo.com/mrss/";
        return [.. doc.Descendants("item").Select(item =>
        {
            var description = (string?)item.Element("description") ?? "";
            var image = (string?)item.Element(media + "content")?.Attribute("url")
                        ?? (string?)item.Element(mediaAlt + "content")?.Attribute("url")
                        ?? (string?)item.Element("enclosure")?.Attribute("url")
                        ?? FirstImage(description);
            DateTimeOffset? published = DateTimeOffset.TryParse((string?)item.Element("pubDate"), CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var date) ? date : null;
            return new NewsItem(Clean((string?)item.Element("title") ?? "Untitled"), Summary(description), image,
                (string?)item.Element("link"), published);
        })];
    }

    public static List<NewsItem> FromJson(string json) =>
        JsonSerializer.Deserialize<List<NewsItem>>(json, JsonFiles.Options) ?? [];

    private static string? FirstImage(string html)
    {
        var match = DataImage().Match(html);
        if (!match.Success) match = ImgSrc().Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static string Summary(string html)
    {
        var text = Clean(Tags().Replace(html, " "));
        return text.Length <= 220 ? text : text[..text.LastIndexOf(' ', 217)] + "…";
    }

    private static string Clean(string text) =>
        SpaceBeforePunctuation().Replace(Spaces().Replace(WebUtility.HtmlDecode(text), " "), "$1").Trim();

    private static string Hash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];

    [GeneratedRegex("data-image=\"([^\"]+)\"")] private static partial Regex DataImage();
    [GeneratedRegex("<img[^>]+src=\"([^\"]+)\"")] private static partial Regex ImgSrc();
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(" ([,.!?;:])")] private static partial Regex SpaceBeforePunctuation();
    [GeneratedRegex("\\s+")] private static partial Regex Spaces();
}
