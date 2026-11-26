using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MultiVRC.Core;

public sealed record LauncherRelease(string Version, string Notes, string DownloadUrl, string? Sha256Url,
    bool Prerelease, DateTimeOffset? PublishedAt, string? PageUrl);

public interface IReleaseSource
{
    Task<IReadOnlyList<LauncherRelease>> GetAsync(CancellationToken ct = default);
}

public sealed class GitHubReleases(HttpClient http, string repository = GitHubReleases.DefaultRepository) : IReleaseSource
{
    public const string DefaultRepository = "ehkogh/MultiVRC";
    /// <summary>The release download for this platform: MultiVRC-win-x64.zip or MultiVRC-linux-x64.zip.</summary>
    public static string Asset { get; } = $"MultiVRC-{(OperatingSystem.IsWindows() ? "win" : "linux")}-{(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64")}.zip";

    /// <summary>The launcher's own program file.</summary>
    public static string ExeName { get; } = OperatingSystem.IsWindows() ? "MultiVRC.exe" : "MultiVRC";

    public async Task<IReadOnlyList<LauncherRelease>> GetAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=20");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public static IReadOnlyList<LauncherRelease> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<LauncherRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var assets = release.TryGetProperty("assets", out var list)
                ? list.EnumerateArray().ToDictionary(a => a.GetProperty("name").GetString() ?? "", a => a.GetProperty("browser_download_url").GetString() ?? "")
                : [];
            if (!assets.TryGetValue(Asset, out var download)) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            releases.Add(new LauncherRelease(
                Version: tag.TrimStart('v', 'V'),
                Notes: release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                DownloadUrl: download,
                Sha256Url: assets.GetValueOrDefault(Asset + ".sha256"),
                Prerelease: release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean(),
                PublishedAt: release.TryGetProperty("published_at", out var at) && at.ValueKind == JsonValueKind.String ? at.GetDateTimeOffset() : null,
                PageUrl: release.TryGetProperty("html_url", out var page) ? page.GetString() : null));
        }
        return releases;
    }
}

public sealed class LauncherUpdater(LauncherPaths paths, HttpClient http, IReleaseSource source, string currentVersion, string appDirectory)
{
    private string UpdatesDir => Path.Combine(paths.Root, "updates");
    private string ReadyFile => Path.Combine(UpdatesDir, "ready.json");
    private string Staged(string version) => Path.Combine(UpdatesDir, version);

    public string CurrentVersion => currentVersion;

    public async Task<LauncherRelease?> CheckAsync(bool prereleases, CancellationToken ct = default)
    {
        var releases = await source.GetAsync(ct);
        return releases
            .Where(r => prereleases || !r.Prerelease)
            .Where(r => CompareVersions(r.Version, currentVersion) > 0)
            .OrderByDescending(r => r.Version, Comparer<string>.Create(CompareVersions))
            .FirstOrDefault();
    }

    public string? ReadyVersion =>
        JsonFiles.Read<ReadyUpdate>(ReadyFile) is { } ready && CompareVersions(ready.Version, currentVersion) > 0 && Directory.Exists(Staged(ready.Version))
            ? ready.Version : null;

    private sealed record ReadyUpdate(string Version);

    public async Task DownloadAsync(LauncherRelease release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(UpdatesDir);
        var zip = Path.Combine(UpdatesDir, $"MultiVRC-{release.Version}.zip");
        await DownloadFileAsync(release.DownloadUrl, zip, progress, ct);

        if (release.Sha256Url is { } shaUrl)
        {
            var expected = (await ReadTextAsync(shaUrl, ct)).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            await using var file = File.OpenRead(zip);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                file.Close();
                File.Delete(zip);
                throw new InvalidOperationException($"The download of MultiVRC {release.Version} is not the published file (SHA-256 mismatch). Not installed.");
            }
        }

        var staged = Staged(release.Version);
        FileLinks.DeleteTree(staged);
        ZipFile.ExtractToDirectory(zip, staged);
        File.Delete(zip);
        if (Directory.GetFiles(staged).Length == 0 && Directory.GetDirectories(staged) is [var only])
        {
            var inner = staged + ".inner";
            Directory.Move(only, inner);
            Directory.Delete(staged);
            Directory.Move(inner, staged);
        }
        if (!File.Exists(Path.Combine(staged, GitHubReleases.ExeName)))
        {
            FileLinks.DeleteTree(staged);
            throw new InvalidOperationException($"MultiVRC {release.Version}'s download has no {GitHubReleases.ExeName} in it. Not installed.");
        }
        JsonFiles.Write(ReadyFile, new ReadyUpdate(release.Version));
    }

    private async Task<string> ReadTextAsync(string url, CancellationToken ct) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile
            ? await File.ReadAllTextAsync(uri.LocalPath, ct)
            : await http.GetStringAsync(url, ct);

    private async Task DownloadFileAsync(string url, string target, IProgress<double>? progress, CancellationToken ct)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            File.Copy(uri.LocalPath, target, overwrite: true);
            progress?.Report(100);
            return;
        }
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(target);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress?.Report(done * 100.0 / total.Value);
        }
    }

    public string Install()
    {
        if (ReadyVersion is not { } version) throw new InvalidOperationException("No update is ready to install.");
        var staged = Staged(version);
        var moved = new List<(string Target, string? Aside)>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(staged, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(appDirectory, Path.GetRelativePath(staged, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? aside = null;
                if (File.Exists(target))
                {
                    aside = target + ".old";
                    if (File.Exists(aside)) File.Delete(aside);
                    File.Move(target, aside);
                }
                moved.Add((target, aside));
                File.Copy(file, target);
            }
        }
        catch
        {
            foreach (var (target, aside) in Enumerable.Reverse(moved))
            {
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (aside is not null && File.Exists(aside)) File.Move(aside, target);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
        FileLinks.DeleteTree(staged);
        File.Delete(ReadyFile);
        var exe = Path.Combine(appDirectory, GitHubReleases.ExeName);
        // A zip made on Windows doesn't carry the executable bit.
        if (!OperatingSystem.IsWindows() && File.Exists(exe))
            File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        return exe;
    }

    public static void RemoveLeftovers(string appDirectory)
    {
        foreach (var old in Directory.EnumerateFiles(appDirectory, "*.old", SearchOption.AllDirectories))
        {
            try { File.Delete(old); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static int CompareVersions(string a, string b)
    {
        var (aCore, aPre) = Split(a);
        var (bCore, bPre) = Split(b);
        for (var i = 0; i < Math.Max(aCore.Length, bCore.Length); i++)
        {
            var x = i < aCore.Length ? aCore[i] : 0;
            var y = i < bCore.Length ? bCore[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        if (aPre == bPre) return 0;
        if (aPre is null) return 1;
        if (bPre is null) return -1;
        var aParts = aPre.Split('.');
        var bParts = bPre.Split('.');
        for (var i = 0; i < Math.Max(aParts.Length, bParts.Length); i++)
        {
            if (i >= aParts.Length) return -1;
            if (i >= bParts.Length) return 1;
            var compared = int.TryParse(aParts[i], out var n) && int.TryParse(bParts[i], out var m)
                ? n.CompareTo(m) : string.CompareOrdinal(aParts[i], bParts[i]);
            if (compared != 0) return compared;
        }
        return 0;

        static (int[] Core, string? Pre) Split(string version)
        {
            version = version.Split('+')[0];
            var dash = version.IndexOf('-');
            var core = dash < 0 ? version : version[..dash];
            return (core.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray(), dash < 0 ? null : version[(dash + 1)..]);
        }
    }
}
