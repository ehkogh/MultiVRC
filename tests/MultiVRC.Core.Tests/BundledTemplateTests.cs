using MultiVRC.Core;
using Xunit;

namespace MultiVRC.Core.Tests;

public sealed class BundledTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "multivrc-bundled-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class NoBranches : IBranchSource
    {
        public Task<IReadOnlyList<SteamBranch>> GetAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SteamBranch>>([]);
    }

    private sealed class Refuse : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            throw new HttpRequestException($"fetched {request.RequestUri}");
        }
    }

    [Fact]
    public void LarpChat_ships_with_the_launcher()
    {
        var larpchat = Assert.Single(BundledTemplates.All(), t => t.Id == "larpchat");
        Assert.True(larpchat.Bundled);
        Assert.Equal("https://assets.larpchat.ehko.dev/multivrc.json", larpchat.SourceUrl);
        Assert.NotEmpty(larpchat.Downloads);
        Assert.All(larpchat.Downloads, d => Assert.False(string.IsNullOrEmpty(d.Sha256)));
    }

    [Fact]
    public void A_bundled_template_is_verified_without_any_list()
    {
        var paths = new LauncherPaths(Path.Combine(_root, "launcher"));
        paths.EnsureCreated();
        var verification = new Verification(paths, new HttpClient(), "https://example.invalid/list.json");
        var larpchat = BundledTemplates.All().Single(t => t.Id == "larpchat");
        Assert.Equal(TrustLevel.Verified, verification.Of(larpchat).Level);
    }

    [Fact]
    public async Task Updates_never_fetch_a_bundled_template()
    {
        var paths = new LauncherPaths(Path.Combine(_root, "launcher"));
        paths.EnsureCreated();
        var refuse = new Refuse();
        var library = new Library(paths, new HttpClient(refuse));
        foreach (var template in BundledTemplates.All()) library.SaveTemplate(template);

        var updates = new Updates(library, new NoBranches());
        await updates.CheckAsync();
        Assert.Equal(0, refuse.Calls);
        Assert.Empty(updates.Problems);
    }
}
