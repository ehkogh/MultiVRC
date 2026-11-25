using MultiVRC.Core;
using Xunit;

namespace MultiVRC.Core.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "multivrc-updates-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly LauncherPaths _paths;
    private readonly Library _library;
    private readonly FakeBranches _steam = new();

    private sealed class FakeBranches : IBranchSource
    {
        public List<SteamBranch> Branches { get; } = [];
        public Task<IReadOnlyList<SteamBranch>> GetAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SteamBranch>>(Branches);
    }

    public UpdateTests()
    {
        _paths = new LauncherPaths(Path.Combine(_root, "launcher"));
        _paths.EnsureCreated();
        _library = new Library(_paths, new HttpClient());
    }

    private Build SteamBuild(string manifest, string branch, string marker)
    {
        var build = new Build { Id = JsonFiles.NewId(), Name = $"Build {manifest}", ManifestId = manifest, Branch = branch, Source = "steam", AddedAt = DateTimeOffset.UtcNow };
        Directory.CreateDirectory(_paths.BuildFiles(build.Id));
        File.WriteAllText(Path.Combine(_paths.BuildFiles(build.Id), "VRChat.exe"), marker);
        _library.SaveBuild(build);
        return build;
    }

    [Fact]
    public async Task An_official_instance_follows_its_branch_to_the_newest_build()
    {
        var old = SteamBuild("111", "public", "old");
        var instance = await _library.CreateInstanceAsync("VRChat", old.Id, null);
        Assert.Equal("public", instance.TrackBranch);
        var updates = new Updates(_library, _steam);

        _steam.Branches.Add(new SteamBranch("public", "", 10, "111", DateTimeOffset.UtcNow, false));
        Assert.Empty(await updates.CheckAsync());

        _steam.Branches[0] = new SteamBranch("public", "", 11, "222", DateTimeOffset.UtcNow, false);
        var pending = Assert.Single(await updates.CheckAsync());
        Assert.Equal(UpdateKind.SteamBuild, pending.Kind);
        Assert.Equal("222", pending.ManifestId);

        File.WriteAllText(Path.Combine(_paths.InstanceData(instance.Id), "output_log.txt"), "kept");
        var next = SteamBuild("222", "public", "new");
        await _library.SwitchBuildAsync(instance, next.Id, null);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_paths.InstanceGame(instance.Id), "VRChat.exe")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(_paths.InstanceData(instance.Id), "output_log.txt")));
        Assert.Empty(await updates.CheckAsync());
        Assert.Equal("old", File.ReadAllText(Path.Combine(_paths.BuildFiles(old.Id), "VRChat.exe")));
    }

    [Fact]
    public async Task A_new_template_version_updates_a_modded_instance_and_its_files()
    {
        var build = SteamBuild("111", "public", "exe");
        var template = new Template
        {
            Id = "server", Name = "Server", Server = "Server", Kind = InstanceKind.Modded, Version = "1.0",
            Build = new TemplateBuild { ManifestId = "111" },
            Files = [new TemplateFile { Path = "plugin.cfg", Content = "v1" }],
        };
        _library.SaveTemplate(template);
        var instance = await _library.CreateInstanceAsync("Server", build.Id, template);
        Assert.Equal("1.0", instance.TemplateVersion);
        Assert.Null(instance.TrackBranch);
        var updates = new Updates(_library, _steam);
        Assert.Empty(await updates.CheckAsync());

        template.Version = "1.1";
        template.Files = [new TemplateFile { Path = "plugin.cfg", Content = "v2" }];
        _library.SaveTemplate(template);
        var pending = Assert.Single(await updates.CheckAsync());
        Assert.Equal(UpdateKind.Template, pending.Kind);
        Assert.Null(pending.ManifestId);

        await _library.SwitchBuildAsync(instance, build.Id, template);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(_paths.InstanceGame(instance.Id), "plugin.cfg")));
        Assert.Equal("1.1", _library.FindInstance(instance.Id)!.TemplateVersion);
        Assert.Empty(await updates.CheckAsync());
    }

    public void Dispose() => FileLinks.DeleteTree(_root);
}
