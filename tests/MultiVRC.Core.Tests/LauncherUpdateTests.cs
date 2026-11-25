using System.IO.Compression;
using System.Security.Cryptography;
using MultiVRC.Core;
using Xunit;

namespace MultiVRC.Core.Tests;

public sealed class LauncherUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "multivrc-update-tests-" + Guid.NewGuid().ToString("N")[..8]);
    private string App => Path.Combine(_root, "app");
    private readonly LauncherPaths _paths;

    public LauncherUpdateTests()
    {
        _paths = new LauncherPaths(Path.Combine(_root, "data"));
        _paths.EnsureCreated();
        Directory.CreateDirectory(App);
        File.WriteAllText(Path.Combine(App, "MultiVRC.exe"), "0.1.0 exe");
        File.WriteAllText(Path.Combine(App, "MultiVRC.Core.dll"), "0.1.0 core");
        File.WriteAllText(Path.Combine(App, "keep.txt"), "not in the update");
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", 1)]
    [InlineData("0.10.0", "0.9.1", 1)]
    [InlineData("1.0.0", "1.0.0-beta.2", 1)]
    [InlineData("1.0.0-beta.10", "1.0.0-beta.2", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("0.1.0+abc", "0.1.0", 0)]
    public void Versions_compare_as_people_expect(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(LauncherUpdater.CompareVersions(a, b)));
        Assert.Equal(-expected, Math.Sign(LauncherUpdater.CompareVersions(b, a)));
    }

    [Fact]
    public void GitHub_releases_without_the_windows_build_or_still_drafts_are_skipped()
    {
        const string json = """
            [
              {"tag_name":"v0.3.0","draft":true,"prerelease":false,"body":"","assets":[{"name":"MultiVRC-win-x64.zip","browser_download_url":"https://x/3.zip"}]},
              {"tag_name":"v0.2.1","draft":false,"prerelease":false,"body":"source only","assets":[]},
              {"tag_name":"v0.2.0","draft":false,"prerelease":true,"body":"Notes","html_url":"https://github.com/r/releases/v0.2.0","published_at":"2026-11-09T10:00:00Z",
               "assets":[{"name":"MultiVRC-win-x64.zip","browser_download_url":"https://x/2.zip"},{"name":"MultiVRC-win-x64.zip.sha256","browser_download_url":"https://x/2.sha"}]}
            ]
            """;
        var release = Assert.Single(GitHubReleases.Parse(json));
        Assert.Equal("0.2.0", release.Version);
        Assert.True(release.Prerelease);
        Assert.Equal("https://x/2.sha", release.Sha256Url);
    }

    [Fact]
    public async Task Only_newer_releases_are_offered_and_pre_releases_only_when_asked()
    {
        var updater = Updater("0.2.0", Release("0.1.0"), Release("0.2.0"), Release("0.3.0-beta.1", prerelease: true));
        Assert.Null(await updater.CheckAsync(prereleases: false));
        Assert.Equal("0.3.0-beta.1", (await updater.CheckAsync(prereleases: true))?.Version);
    }

    [Fact]
    public async Task A_downloaded_release_installs_over_the_running_one_and_its_leftovers_go_on_the_next_start()
    {
        var zip = MakeZip("0.2.0", out var sha);
        var release = Release("0.2.0", new Uri(zip).AbsoluteUri, sha);
        var updater = Updater("0.1.0", release);

        Assert.Equal("0.2.0", (await updater.CheckAsync(false))?.Version);
        await updater.DownloadAsync(release);
        Assert.Equal("0.2.0", updater.ReadyVersion);

        var exe = updater.Install();
        Assert.Equal(Path.Combine(App, "MultiVRC.exe"), exe);
        Assert.Equal("0.2.0 exe", File.ReadAllText(Path.Combine(App, "MultiVRC.exe")));
        Assert.Equal("0.2.0 core", File.ReadAllText(Path.Combine(App, "MultiVRC.Core.dll")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(App, "wwwroot", "added.css")));
        Assert.Equal("not in the update", File.ReadAllText(Path.Combine(App, "keep.txt")));
        Assert.True(File.Exists(Path.Combine(App, "MultiVRC.exe.old")));
        Assert.Null(updater.ReadyVersion);

        LauncherUpdater.RemoveLeftovers(App);
        Assert.Empty(Directory.GetFiles(App, "*.old", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_download_that_is_not_the_published_file_is_refused()
    {
        var zip = MakeZip("0.2.0", out _);
        var release = Release("0.2.0", new Uri(zip).AbsoluteUri, new string('0', 64));
        var updater = Updater("0.1.0", release);
        await Assert.ThrowsAsync<InvalidOperationException>(() => updater.DownloadAsync(release));
        Assert.Null(updater.ReadyVersion);
    }

    [Fact]
    public async Task An_install_that_fails_half_way_puts_the_old_files_back()
    {
        var zip = MakeZip("0.2.0", out var sha);
        var release = Release("0.2.0", new Uri(zip).AbsoluteUri, sha);
        var updater = Updater("0.1.0", release);
        await updater.DownloadAsync(release);

        using (File.Open(Path.Combine(App, "MultiVRC.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => updater.Install());

        Assert.Equal("0.1.0 exe", File.ReadAllText(Path.Combine(App, "MultiVRC.exe")));
        Assert.Equal("0.1.0 core", File.ReadAllText(Path.Combine(App, "MultiVRC.Core.dll")));
        Assert.Equal("0.2.0", updater.ReadyVersion);
    }

    private LauncherUpdater Updater(string current, params LauncherRelease[] releases) =>
        new(_paths, new HttpClient(), new FixedReleases(releases), current, App);

    private static LauncherRelease Release(string version, string url = "https://example.com/x.zip", string? sha = null, bool prerelease = false) =>
        new(version, "", url, sha, prerelease, null, null);

    private string MakeZip(string version, out string sha)
    {
        var source = Path.Combine(_root, "release", "MultiVRC");
        Directory.CreateDirectory(Path.Combine(source, "wwwroot"));
        File.WriteAllText(Path.Combine(source, "MultiVRC.exe"), $"{version} exe");
        File.WriteAllText(Path.Combine(source, "MultiVRC.Core.dll"), $"{version} core");
        File.WriteAllText(Path.Combine(source, "wwwroot", "added.css"), "new");
        var zip = Path.Combine(_root, $"MultiVRC-{version}.zip");
        ZipFile.CreateFromDirectory(source, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        var shaFile = zip + ".sha256";
        File.WriteAllText(shaFile, $"{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant()}  MultiVRC-win-x64.zip\n");
        sha = new Uri(shaFile).AbsoluteUri;
        return zip;
    }

    private sealed class FixedReleases(IReadOnlyList<LauncherRelease> releases) : IReleaseSource
    {
        public Task<IReadOnlyList<LauncherRelease>> GetAsync(CancellationToken ct = default) => Task.FromResult(releases);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
