using System.Text.Json;

namespace MultiVRC.Core;

public enum TrustLevel
{
    Official,
    Verified,
    Unverified,
}

public sealed class VerifiedServer
{
    public required string Name { get; set; }
    public required string TemplateUrl { get; set; }
    public List<string> VerifiedBy { get; set; } = [];
    public string Note { get; set; } = "";
}

public sealed record Trust(TrustLevel Level, VerifiedServer? Entry);

public sealed class Verification(LauncherPaths paths, HttpClient http, string listUrl)
{
    public const string DefaultListUrl = "https://multivrc.ehko.dev/api/verified-servers";

    private List<VerifiedServer> _servers = JsonFiles.Read<List<VerifiedServer>>(CacheFile(paths)) ?? [];

    public IReadOnlyList<VerifiedServer> Servers => _servers;

    private static string CacheFile(LauncherPaths paths) => Path.Combine(paths.Root, "verified-servers.json");

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var json = Uri.TryCreate(listUrl, UriKind.Absolute, out var uri) && uri.IsFile
                ? await File.ReadAllTextAsync(uri.LocalPath, ct)
                : await http.GetStringAsync(listUrl, ct);
            _servers = JsonSerializer.Deserialize<List<VerifiedServer>>(json, JsonFiles.Options) ?? [];
            JsonFiles.Write(CacheFile(paths), _servers);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
        }
    }

    public Trust Of(Instance instance, Template? template) =>
        instance.Kind == InstanceKind.Official ? new Trust(TrustLevel.Official, null) : Community(template);

    public Trust Of(Template template) =>
        template.Kind == InstanceKind.Official ? new Trust(TrustLevel.Official, null) : Community(template);

    private Trust Community(Template? template)
    {
        var entry = template?.SourceUrl is { } url ? _servers.FirstOrDefault(s => Same(s.TemplateUrl, url)) : null;
        return entry is not null || template?.Bundled == true
            ? new Trust(TrustLevel.Verified, entry)
            : new Trust(TrustLevel.Unverified, null);
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
