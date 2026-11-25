using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace MultiVRC.Core;

public sealed record SteamProfile(string Name, string? AvatarUrl, DateTimeOffset FetchedAt);

public static class SteamProfiles
{
    private static readonly TimeSpan Fresh = TimeSpan.FromDays(1);

    public static string CacheFile(LauncherPaths paths) => Path.Combine(paths.Root, "steam-profile.json");

    public static SteamProfile? Cached(LauncherPaths paths) => JsonFiles.Read<SteamProfile>(CacheFile(paths));

    public static bool IsStale(SteamProfile? profile) => profile is null || DateTimeOffset.UtcNow - profile.FetchedAt > Fresh;

    public static async Task<SteamProfile?> FetchAsync(LauncherPaths paths, HttpClient http, ulong steamId, CancellationToken ct = default)
    {
        try
        {
            var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1", ct);
            var profile = Parse(xml);
            if (profile is not null) JsonFiles.Write(CacheFile(paths), profile);
            return profile;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return null;
        }
    }

    public static SteamProfile? Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        var name = root?.Element("steamID")?.Value.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        var avatar = root!.Element("avatarFull")?.Value.Trim();
        return new SteamProfile(name, string.IsNullOrEmpty(avatar) ? null : avatar, DateTimeOffset.UtcNow);
    }

    public static void Forget(LauncherPaths paths)
    {
        if (File.Exists(CacheFile(paths))) File.Delete(CacheFile(paths));
    }

    public static ulong? SteamIdOf(string refreshToken)
    {
        var parts = refreshToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return json.RootElement.TryGetProperty("sub", out var sub) && ulong.TryParse(sub.GetString(), out var id) ? id : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
