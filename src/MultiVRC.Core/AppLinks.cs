namespace MultiVRC.Core;

public abstract record LinkRequest;

public sealed record LaunchRequest(string Target, string? Option) : LinkRequest;

public sealed record InstallRequest(string TemplateUrl) : LinkRequest;

public sealed record LaunchChoice(LaunchMode Mode, TemplatePlayMode? Play);

public static class AppLinks
{
    public const string Scheme = "multivrc";

    public const string Desktop = "desktop";

    private const int MaxLength = 2048;

    public static LinkRequest? Parse(string? text)
    {
        if (text is null || text.Length > MaxLength) return null;
        text = text.Trim();
        var prefix = Scheme + ":";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = text[prefix.Length..].TrimStart('/');
        var slash = rest.IndexOf('/');
        var action = (slash < 0 ? rest : rest[..slash]).Split('?')[0].ToLowerInvariant();
        var tail = slash < 0 ? "" : rest[(slash + 1)..];
        return action switch
        {
            "launch" or "play" => ParseLaunch(tail),
            "install" or "add" => ParseInstall(slash < 0 ? rest[action.Length..] : tail),
            _ => null,
        };
    }

    private static LaunchRequest? ParseLaunch(string tail)
    {
        var parts = tail.Split('?', '#')[0].Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToList();
        if (parts.Count is 0 or > 2 || parts.Any(p => !IsId(p))) return null;
        return new LaunchRequest(parts[0], parts.Count == 2 ? parts[1] : null);
    }

    private static InstallRequest? ParseInstall(string tail)
    {
        var url = tail.StartsWith('?')
            ? tail[1..].Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == "url")?[1]
            : tail;
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = Uri.UnescapeDataString(url.Trim());
        // Some browsers fold "https://" in a path down to "https:/".
        if (url.StartsWith("https:/", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url["https:/".Length..];
        // ...and some add a trailing slash to the whole link.
        if (url.EndsWith(".json/", StringComparison.OrdinalIgnoreCase)) url = url[..^1];
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0
            ? new InstallRequest(uri.AbsoluteUri)
            : null;
    }

    private static bool IsId(string part) => part.Length is > 0 and <= 100 && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string LaunchUrl(string templateId, string? option = null) =>
        $"{Scheme}://launch/{templateId}{(option is null ? "" : "/" + option)}";

    public static string InstallUrl(string templateUrl) => $"{Scheme}://install/{Uri.EscapeDataString(templateUrl)}";

    public static string IdOf(TemplatePlayMode play) => play.Id is { Length: > 0 } id ? id : Slug(play.Label);

    public static string Slug(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    public static Instance? PickInstance(IEnumerable<Instance> instances, string target)
    {
        var all = instances.ToList();
        return all.FirstOrDefault(i => string.Equals(i.Id, target, StringComparison.OrdinalIgnoreCase))
               ?? all.Where(i => string.Equals(i.TemplateId, target, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(i => i.LastPlayedAt ?? DateTimeOffset.MinValue)
                   .ThenBy(i => i.SortOrder)
                   .FirstOrDefault();
    }

    public static LaunchChoice? Choose(Instance instance, IReadOnlyList<TemplatePlayMode> playModes, string? option)
    {
        if (option is null or "" || Is(option, "play") || Is(option, "normal") || Is(option, "default"))
            return new LaunchChoice(LaunchMode.Normal, null);
        if (playModes.FirstOrDefault(p => Is(IdOf(p), option)) is { } play) return new LaunchChoice(LaunchMode.Normal, play);
        // Starting without anti-cheat is never offered through a link: a web page should not be able to turn it off.
        if (instance.Kind == InstanceKind.Official && Is(option, Desktop)) return new LaunchChoice(LaunchMode.Desktop, null);
        return null;
    }

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
