using MultiVRC.Core;

namespace MultiVRC.Core.Tests;

public sealed class OfficialKindTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "multivrc-official-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Library _library;

    public OfficialKindTests()
    {
        var paths = new LauncherPaths(Path.Combine(_root, "launcher"));
        paths.EnsureCreated();
        _library = new Library(paths, new HttpClient());
    }

    private string Published(string json)
    {
        var file = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, json);
        return new Uri(file).AbsoluteUri;
    }

    [Theory]
    [InlineData("""{ "id": "fake", "name": "VRChat", "kind": "Official" }""")]
    [InlineData("""{ "id": "fake", "name": "VRChat", "kind": "official" }""")]
    [InlineData("""{ "id": "fake", "name": "VRChat", "kind": 0 }""")]
    [InlineData("""{ "id": "vrchat-official", "name": "VRChat", "kind": "Modded" }""")]
    [InlineData("""{ "id": "VRChat-Beta", "name": "VRChat", "kind": "Modded" }""")]
    public async Task Templates_from_a_url_cannot_be_official(string json)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _library.InstallTemplateAsync(Published(json)));
        Assert.Empty(_library.Templates());
    }

    [Fact]
    public async Task A_modded_template_still_installs()
    {
        var template = await _library.InstallTemplateAsync(Published("""{ "id": "neon", "name": "Neon", "kind": "Modded" }"""));
        Assert.Equal(InstanceKind.Modded, template.Kind);
        Assert.NotNull(_library.FindTemplate("neon"));
    }

    [Fact]
    public async Task An_official_template_already_on_disk_from_a_url_is_not_trusted()
    {
        var verification = new Verification(_library.Paths, new HttpClient(), new Uri(Path.Combine(_root, "none.json")).AbsoluteUri);
        await verification.RefreshAsync();
        var smuggled = new Template { Id = "fake", Name = "VRChat", Kind = InstanceKind.Official, SourceUrl = "https://evil.example/multivrc.json" };
        var shipped = new Template { Id = "vrchat-official", Name = "VRChat", Kind = InstanceKind.Official };
        var instance = new Instance { Id = "i", Name = "i", BuildId = "b", Kind = InstanceKind.Official, TemplateId = "fake" };

        Assert.Equal(TrustLevel.Unverified, verification.Of(smuggled).Level);
        Assert.Equal(TrustLevel.Unverified, verification.Of(instance, smuggled).Level);
        Assert.Equal(TrustLevel.Official, verification.Of(shipped).Level);
        Assert.Equal(TrustLevel.Official, verification.Of(instance, shipped).Level);
        var game = Directory.CreateDirectory(Path.Combine(_root, "game")).FullName;
        File.WriteAllText(Path.Combine(game, "VRChat.exe"), "");
        var build = _library.ImportFolder(game, "VRChat", "1");
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => _library.CreateInstanceAsync("x", build.Id, smuggled));
        Assert.Contains("calls itself official", refused.Message);
        Assert.Equal(InstanceKind.Official, (await _library.CreateInstanceAsync("y", build.Id, shipped)).Kind);
    }

    public void Dispose() => FileLinks.DeleteTree(_root);
}
