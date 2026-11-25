using Microsoft.Win32;
using MultiVRC.Core;
using Xunit;

namespace MultiVRC.Core.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "multivrc-tests-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _registryRoot = @"Software\MultiVRCTests\" + Guid.NewGuid().ToString("N")[..8];
    private readonly LauncherPaths _paths;
    private readonly Library _library;

    public CoreTests()
    {
        _paths = new LauncherPaths(Path.Combine(_root, "launcher"));
        _paths.EnsureCreated();
        _library = new Library(_paths, new HttpClient());
    }

    private string GameKey => _registryRoot + @"\Game";
    private string LauncherKey => _registryRoot + @"\Launcher";
    private string DataFolder => Path.Combine(_root, "LocalLow", "VRChat", "VRChat");
    private Isolation NewIsolation() => new(_paths, DataFolder, GameKey, LauncherKey);

    private string FakeGame()
    {
        var folder = Path.Combine(_root, "steam-build");
        Directory.CreateDirectory(Path.Combine(folder, "VRChat_Data"));
        File.WriteAllText(Path.Combine(folder, "VRChat.exe"), "exe");
        File.WriteAllText(Path.Combine(folder, "VRChat_Data", "level0"), "original");
        return folder;
    }

    [Fact]
    public async Task An_instance_shares_the_build_files_and_its_changes_never_reach_the_build()
    {
        var build = _library.ImportFolder(FakeGame(), "Test build", "2026.4");
        var template = new Template
        {
            Id = "test", Name = "Test", Kind = InstanceKind.Modded,
            Files = [new TemplateFile { Path = @"VRChat_Data\level0", Content = "patched" }, new TemplateFile { Path = @"BepInEx\config\x.cfg", Content = "server=local" }],
        };
        var instance = await _library.CreateInstanceAsync("Modded", build.Id, template);

        var game = _paths.InstanceGame(instance.Id);
        Assert.Equal("exe", File.ReadAllText(Path.Combine(game, "VRChat.exe")));
        Assert.True(FileLinks.LinkCount(Path.Combine(game, "VRChat.exe")) > 1, "the exe should be a hardlink of the build's");
        Assert.Equal("patched", File.ReadAllText(Path.Combine(game, "VRChat_Data", "level0")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(_paths.BuildFiles(build.Id), "VRChat_Data", "level0")));
        Assert.Equal([@"BepInEx\config\x.cfg", @"VRChat_Data\level0"], _library.AddedFiles(instance));

        _library.DeleteInstance(instance.Id, NewIsolation());
        Assert.True(File.Exists(Path.Combine(_paths.BuildFiles(build.Id), "VRChat.exe")));
    }

    [Fact]
    public async Task An_official_instance_is_never_modified_and_a_template_cannot_write_outside_the_game()
    {
        var build = _library.ImportFolder(FakeGame(), "Test build", "2026.4");
        var official = new Template { Id = "o", Name = "O", Kind = InstanceKind.Official, Files = [new TemplateFile { Path = "x.txt" }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _library.CreateInstanceAsync("Official", build.Id, official));
        Assert.Empty(_library.Instances());

        var escaping = new Template { Id = "e", Name = "E", Kind = InstanceKind.Modded, Files = [new TemplateFile { Path = @"..\..\escaped.txt", Content = "x" }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _library.CreateInstanceAsync("Escaping", build.Id, escaping));
        Assert.False(File.Exists(Path.Combine(_paths.Instances, "escaped.txt")));
    }

    [Fact]
    public async Task A_session_swaps_the_data_and_settings_in_and_puts_the_originals_back()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(Path.Combine(DataFolder, "mine.txt"), "personal");
        using (var key = Registry.CurrentUser.CreateSubKey(GameKey)) key.SetValue("authToken", "official-login");

        var build = _library.ImportFolder(FakeGame(), "Test build", "2026.4");
        var instance = await _library.CreateInstanceAsync("Private", build.Id, new Template { Id = "p", Name = "P", Kind = InstanceKind.Modded });
        var isolation = NewIsolation();

        isolation.Apply(instance);
        Assert.True(FileLinks.IsJunction(DataFolder));
        Assert.False(File.Exists(Path.Combine(DataFolder, "mine.txt")));
        using (var key = Registry.CurrentUser.OpenSubKey(GameKey)) Assert.Null(key?.GetValue("authToken"));
        File.WriteAllText(Path.Combine(DataFolder, "session.txt"), "instance data");
        using (var key = Registry.CurrentUser.CreateSubKey(GameKey)) key.SetValue("authToken", "private-login");

        isolation.Restore();
        Assert.False(FileLinks.IsJunction(DataFolder));
        Assert.Equal("personal", File.ReadAllText(Path.Combine(DataFolder, "mine.txt")));
        using (var key = Registry.CurrentUser.OpenSubKey(GameKey)) Assert.Equal("official-login", key?.GetValue("authToken"));
        Assert.Equal("instance data", File.ReadAllText(Path.Combine(_paths.InstanceData(instance.Id), "session.txt")));
        Assert.Null(isolation.Current);

        isolation.Apply(instance);
        using (var key = Registry.CurrentUser.OpenSubKey(GameKey)) Assert.Equal("private-login", key?.GetValue("authToken"));
        isolation.Restore();
    }

    [Fact]
    public async Task Unity_settings_survive_a_session_byte_for_byte()
    {
        var volume = BitConverter.GetBytes(0.75);
        using (var key = Registry.CurrentUser.CreateSubKey(GameKey))
        {
            SetRaw(key, "AUDIO_MASTER_h1232282224", RegDword, volume);
            SetRaw(key, "VRC_INPUT_MIC_DEVICE_NAME_h3782209548", RegBinary, []);
            key.SetValue("LOD_QUALITY_h1", 2, RegistryValueKind.DWord);
            using var sub = key.CreateSubKey("Nested");
            sub.SetValue("x", "y");
        }

        var build = _library.ImportFolder(FakeGame(), "Test build", "2026.4");
        var instance = await _library.CreateInstanceAsync("Official", build.Id, null);
        var isolation = NewIsolation();
        isolation.Apply(instance);
        isolation.Restore();

        using var restored = Registry.CurrentUser.OpenSubKey(GameKey)!;
        var (volumeType, volumeData) = GetRaw(restored, "AUDIO_MASTER_h1232282224");
        Assert.Equal(RegDword, volumeType);
        Assert.Equal(volume, volumeData);
        var (micType, micData) = GetRaw(restored, "VRC_INPUT_MIC_DEVICE_NAME_h3782209548");
        Assert.Equal(RegBinary, micType);
        Assert.Empty(micData);
        Assert.Equal(2, restored.GetValue("LOD_QUALITY_h1"));
        Assert.Equal("y", restored.OpenSubKey("Nested")?.GetValue("x"));
    }

    private const uint RegBinary = 3, RegDword = 4;

    private static void SetRaw(RegistryKey key, string name, uint type, byte[] data) =>
        Assert.Equal(0, RegSetValueExW(key.Handle, name, 0, type, data, (uint)data.Length));

    private static (uint Type, byte[] Data) GetRaw(RegistryKey key, string name)
    {
        uint size = 0;
        RegQueryValueExW(key.Handle, name, IntPtr.Zero, out _, null, ref size);
        var data = new byte[size];
        Assert.Equal(0, RegQueryValueExW(key.Handle, name, IntPtr.Zero, out var type, data, ref size));
        return (type, data);
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegQueryValueExW(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, string name, IntPtr reserved, out uint type, byte[]? data, ref uint size);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegSetValueExW(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, string name, int reserved, uint type, byte[] data, uint size);

    [Fact]
    public async Task A_session_left_swapped_by_a_crash_is_recovered_only_once_the_game_is_gone()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(Path.Combine(DataFolder, "mine.txt"), "personal");
        var build = _library.ImportFolder(FakeGame(), "Test build", "2026.4");
        var instance = await _library.CreateInstanceAsync("Private", build.Id, new Template { Id = "p", Name = "P", Kind = InstanceKind.Modded });
        NewIsolation().Apply(instance);

        var afterCrash = NewIsolation();
        Assert.Null(afterCrash.Recover(gameRunning: () => true));
        Assert.True(FileLinks.IsJunction(DataFolder));

        Assert.Equal(instance.Id, afterCrash.Recover(gameRunning: () => false));
        Assert.Equal("personal", File.ReadAllText(Path.Combine(DataFolder, "mine.txt")));
        Assert.Null(afterCrash.Current);
    }

    [Fact]
    public async Task Only_one_servers_instances_share_a_download_cache()
    {
        var build = _library.ImportFolder(FakeGame(), "Test build", "2026.4");
        var official = await _library.CreateInstanceAsync("Official", build.Id, null);
        var modded = await _library.CreateInstanceAsync("Modded", build.Id, new Template { Id = "server", Name = "Server", ShareCache = true });
        var custom = await _library.CreateInstanceAsync("Own", build.Id, new Template { Id = "solo", Name = "Solo" });
        Assert.True(official.ShareCache);
        Assert.False(custom.ShareCache);

        var isolation = NewIsolation();
        string Session(Instance instance)
        {
            isolation.Apply(instance);
            var cache = Directory.CreateDirectory(Path.Combine(DataFolder, "Cache-WindowsPlayer")).FullName;
            var seen = string.Join(",", Directory.EnumerateFiles(cache).Select(Path.GetFileName).Order());
            File.WriteAllText(Path.Combine(cache, $"{instance.Name}.bundle"), "cached");
            isolation.Restore();
            return seen;
        }
        Assert.Equal("", Session(official));
        Assert.Equal("", Session(modded));
        Assert.Equal("", Session(custom));
        Assert.True(File.Exists(Path.Combine(_paths.SharedCacheFor(official), "Official.bundle")));
        Assert.True(File.Exists(Path.Combine(_paths.SharedCacheFor(modded), "Modded.bundle")));
        Assert.True(File.Exists(Path.Combine(_paths.InstanceData(custom.Id), "Cache-WindowsPlayer", "Own.bundle")));

        _library.DeleteInstance(official.Id, isolation);
        Assert.True(File.Exists(Path.Combine(_paths.SharedCacheFor(official), "Official.bundle")));
    }

    [Fact]
    public async Task A_simulated_session_never_swaps_anything()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(Path.Combine(DataFolder, "mine.txt"), "personal");
        var build = _library.ImportFolder(FakeGame(), "Test build", "2026.4");
        var instance = await _library.CreateInstanceAsync("Demo", build.Id, null);
        var isolation = NewIsolation();
        using var runner = new GameRunner(_library, isolation, simulate: true);

        runner.Launch(instance);
        Assert.Equal(instance.Id, runner.Running?.Id);
        Assert.Null(isolation.Current);
        Assert.False(FileLinks.IsJunction(DataFolder));
        Assert.True(File.Exists(Path.Combine(DataFolder, "mine.txt")));

        runner.Stop();
        Assert.Null(runner.Running);
        Assert.NotNull(_library.FindInstance(instance.Id)!.LastPlayedAt);
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_registryRoot, throwOnMissingSubKey: false); } catch (UnauthorizedAccessException) { }
        if (FileLinks.IsJunction(DataFolder)) FileLinks.RemoveJunction(DataFolder);
        FileLinks.DeleteTree(_root);
    }
}
