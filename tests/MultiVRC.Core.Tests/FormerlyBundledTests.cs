using MultiVRC.Core;
using Xunit;

namespace MultiVRC.Core.Tests;

public sealed class FormerlyBundledTests : IDisposable
{
    private const string LarpChatUrl = "https://assets.larpchat.ehko.dev/multivrc.json";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "multivrc-bundled-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly LauncherPaths _paths;

    public FormerlyBundledTests()
    {
        _paths = new LauncherPaths(Path.Combine(_root, "launcher"));
        _paths.EnsureCreated();
        Directory.CreateDirectory(_paths.Templates);
        File.WriteAllText(Path.Combine(_paths.Templates, "larpchat.json"), $$"""
            { "id": "larpchat", "name": "LarpChat", "server": "LarpChat", "kind": "Modded", "sourceUrl": "{{LarpChatUrl}}", "bundled": true }
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<Verification> ListingAsync(params string[] urls)
    {
        var list = Path.Combine(_root, "list.json");
        File.WriteAllText(list, System.Text.Json.JsonSerializer.Serialize(urls.Select(u => new { name = "Server", templateUrl = u })));
        var verification = new Verification(_paths, new HttpClient(), new Uri(list).AbsoluteUri);
        await verification.RefreshAsync();
        return verification;
    }

    private Template SavedLarpChat() => new Library(_paths, new HttpClient()).FindTemplate("larpchat")!;

    [Fact]
    public async Task LarpChat_saved_by_0_1_0_is_verified_only_while_it_is_listed()
    {
        Assert.Equal(TrustLevel.Unverified, (await ListingAsync()).Of(SavedLarpChat()).Level);
        Assert.Equal(TrustLevel.Verified, (await ListingAsync(LarpChatUrl)).Of(SavedLarpChat()).Level);
    }
}
