using MultiVRC.Core;

namespace MultiVRC.Core.Tests;

public sealed class ProtonTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "multivrc-proton-" + Guid.NewGuid().ToString("N")[..8]);

    private string Home => Path.Combine(_root, "home");

    private static void Touch(string path, string content = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>A Steam in ~/.local/share/Steam with a second library on another drive, as Steam lays it out.</summary>
    private (string Steam, string Library) FakeSteam()
    {
        var steam = Path.Combine(Home, ".local", "share", "Steam");
        var library = Path.Combine(_root, "mnt", "games", "SteamLibrary");
        Touch(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
                "0" { "path" "{{steam.Replace(@"\", @"\\")}}" }
                "1" { "path" "{{library.Replace(@"\", @"\\")}}" }
            }
            """);
        var common = Path.Combine(steam, "steamapps", "common");
        Touch(Path.Combine(common, "Proton 8.0", "proton"));
        Touch(Path.Combine(common, "Proton 9.0 (Beta)", "proton"));
        Touch(Path.Combine(common, "Proton 9.0 (Beta)", "toolmanifest.vdf"), "\"manifest\" { \"require_tool_appid\" \"1628350\" }");
        Touch(Path.Combine(common, "SteamLinuxRuntime_sniper", "_v2-entry-point"));
        Touch(Path.Combine(library, "steamapps", "common", "Proton - Experimental", "proton"));
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "Proton EasyAntiCheat Runtime"));
        Touch(Path.Combine(steam, "compatibilitytools.d", "GE-Proton9-20", "proton"));
        return (steam, library);
    }

    [Fact]
    public void Finds_every_proton_across_libraries_and_the_runtimes()
    {
        var (steam, library) = FakeSteam();
        var found = Proton.Find(Home);

        Assert.Equal(Path.GetFullPath(steam), Path.GetFullPath(found.Root!));
        Assert.Equal(["GE-Proton9-20", "Proton - Experimental", "Proton 8.0", "Proton 9.0 (Beta)"], found.Protons.Select(p => p.Name));
        Assert.False(found.Protons.Single(p => p.Name == "GE-Proton9-20").FromSteam);
        Assert.True(found.Protons.Single(p => p.Name == "Proton 9.0 (Beta)").NeedsRuntime);
        Assert.False(found.Protons.Single(p => p.Name == "Proton 8.0").NeedsRuntime);
        Assert.Equal(Path.Combine(library, "steamapps", "common", "Proton EasyAntiCheat Runtime"), found.AntiCheatRuntime);
        Assert.EndsWith("_v2-entry-point", found.ContainerRuntime);
    }

    [Fact]
    public void Picks_the_chosen_proton_or_else_valves_newest()
    {
        FakeSteam();
        var found = Proton.Find(Home);
        Assert.Equal("Proton - Experimental", found.Pick(null)!.Name);
        Assert.Equal("GE-Proton9-20", found.Pick(found.Protons.Single(p => p.Name == "GE-Proton9-20").Folder)!.Name);
        Assert.Equal("Proton - Experimental", found.Pick("/gone")!.Name);
        Assert.Null(Proton.Find(Path.Combine(_root, "nobody")).Pick(null));
    }

    [Fact]
    public void Starts_the_game_the_way_steam_does()
    {
        FakeSteam();
        var steam = Proton.Find(Home);
        var proton = steam.Protons.Single(p => p.Name == "Proton 9.0 (Beta)");
        var start = Proton.Command(steam, proton, "/data/instances/a/proton", "/data/instances/a/game", "start_protected_game.exe", ["--no-vr"], antiCheat: true);

        Assert.Equal(steam.ContainerRuntime, start.FileName);
        Assert.Equal(["--verb=waitforexitandrun", "--", proton.Script, "waitforexitandrun", Path.Combine("/data/instances/a/game", "start_protected_game.exe"), "--no-vr"],
            start.ArgumentList);
        Assert.Equal("/data/instances/a/proton", start.Environment["STEAM_COMPAT_DATA_PATH"]);
        Assert.Equal(steam.Root, start.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"]);
        Assert.Equal("438100", start.Environment["SteamAppId"]);
        Assert.Equal(steam.AntiCheatRuntime, start.Environment["PROTON_EAC_RUNTIME"]);

        var modded = Proton.Command(steam, steam.Protons.Single(p => p.Name == "Proton 8.0"), "/c", "/g", "VRChat.exe", [], antiCheat: false);
        Assert.EndsWith("proton", modded.FileName);
        Assert.False(modded.Environment.ContainsKey("PROTON_EAC_RUNTIME"));
    }

    [Theory]
    [InlineData("Z:\\home\\me\\MultiVRC\\instances\\a\\game\\VRChat.exe\0--no-vr\0", "VRChat")]
    [InlineData("C:\\windows\\system32\\start_protected_game.exe\0", "start_protected_game")]
    [InlineData("/usr/bin/python3\0/home/me/proton\0waitforexitandrun\0VRChat.exe\0", "python3")]
    [InlineData("", null)]
    public void Recognises_wine_processes_by_their_windows_path(string cmdline, string? name) =>
        Assert.Equal(name, GameProcesses.NameOf(cmdline));

    [Theory]
    [InlineData("Proton - Experimental", "Proton 9.0", true)]
    [InlineData("Proton 9.0 (Beta)", "Proton 8.0", true)]
    [InlineData("Proton 10.0", "Proton 9.0", true)]
    [InlineData("Proton 8.0", "Proton Hotfix", true)]
    public void Ranks_valves_protons(string higher, string lower, bool expected) =>
        Assert.Equal(expected, Proton.Rank(higher) > Proton.Rank(lower));

    [Fact]
    public async Task Each_instance_gets_its_own_prefix_with_its_data_and_the_players_photos_linked_in()
    {
        var paths = new LauncherPaths(Path.Combine(_root, "launcher"));
        paths.EnsureCreated();
        var library = new Library(paths, new HttpClient());
        var game = Path.Combine(_root, "steam-build");
        Touch(Path.Combine(game, "VRChat.exe"), "exe");
        var build = library.ImportFolder(game, "Build", "1");
        var instance = await library.CreateInstanceAsync("Linux", build.Id, null);
        var pictures = Path.Combine(_root, "Pictures");
        var isolation = new PrefixIsolation(paths, pictures);

        // The game ran once before the link existed and wrote its own folder.
        var user = Proton.PrefixUser(paths.InstanceProton(instance.Id));
        var inPrefix = Path.Combine(user, "AppData", "LocalLow", "VRChat", "VRChat");
        Touch(Path.Combine(inPrefix, "early.log"), "first run");

        isolation.Apply(instance);
        isolation.Apply(instance);

        Assert.True(FileLinks.IsJunction(inPrefix));
        Assert.Equal("first run", File.ReadAllText(Path.Combine(paths.InstanceData(instance.Id), "early.log")));
        File.WriteAllText(Path.Combine(inPrefix, "output_log.txt"), "written by the game");
        Assert.True(File.Exists(Path.Combine(paths.InstanceData(instance.Id), "output_log.txt")));
        Assert.True(FileLinks.IsJunction(Path.Combine(inPrefix, "Cache-WindowsPlayer")), "official instances share their cache");

        File.WriteAllText(Path.Combine(user, "Pictures", "VRChat", "photo.png"), "png");
        Assert.True(File.Exists(Path.Combine(pictures, "VRChat", "photo.png")));
        Assert.Null(isolation.Current);

        library.DeleteInstance(instance.Id, isolation);
        Assert.False(Directory.Exists(paths.InstanceDir(instance.Id)));
        Assert.True(File.Exists(Path.Combine(pictures, "VRChat", "photo.png")), "deleting an instance never reaches through its links");
    }

    [Fact]
    public void Deleting_a_tree_removes_links_without_following_them()
    {
        var outside = Path.Combine(_root, "outside");
        Touch(Path.Combine(outside, "keep.txt"), "keep");
        var tree = Path.Combine(_root, "tree");
        Touch(Path.Combine(tree, "a", "file.txt"));
        FileLinks.CreateJunction(Path.Combine(tree, "a", "z-drive"), outside);

        Assert.Equal(0, FileLinks.SizeOf(tree));
        FileLinks.DeleteTree(tree);

        Assert.False(Directory.Exists(tree));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "keep.txt")));
    }

    public void Dispose() => FileLinks.DeleteTree(_root);
}
