using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MultiVRC.Core;

/// <summary>One Proton the player has, from Steam (Proton 9.0, Experimental…) or compatibilitytools.d (GE-Proton…).</summary>
/// <param name="NeedsRuntime">Its toolmanifest asks for the Steam Linux Runtime (sniper) container, as Proton 8 and later do.</param>
public sealed record ProtonInstall(string Name, string Folder, bool NeedsRuntime, bool FromSteam)
{
    public string Script => Path.Combine(Folder, "proton");
}

/// <summary>What MultiVRC found of Steam on this Linux machine.</summary>
public sealed record SteamSetup(string? Root, IReadOnlyList<ProtonInstall> Protons, string? AntiCheatRuntime, string? ContainerRuntime)
{
    public static SteamSetup None { get; } = new(null, [], null, null);

    /// <summary>The Proton to use: the chosen one if it is still there, else the newest of Valve's.</summary>
    public ProtonInstall? Pick(string? chosenFolder) =>
        Protons.FirstOrDefault(p => chosenFolder is not null && Same(p.Folder, chosenFolder))
        ?? Protons.Where(p => p.FromSteam).OrderByDescending(p => Proton.Rank(p.Name)).FirstOrDefault()
        ?? Protons.FirstOrDefault();

    private static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.Ordinal);
}

public static partial class Proton
{
    public const string AppId = "438100";
    private const string ContainerAppId = "1628350";

    public static string PrefixUser(string compatData) => Path.Combine(compatData, "pfx", "drive_c", "users", "steamuser");

    public static SteamSetup Find(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
            {
                Path.Combine(home, ".steam", "root"), Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            }
            .Where(r => File.Exists(Path.Combine(r, "steamapps", "libraryfolders.vdf")) || Directory.Exists(Path.Combine(r, "steamapps")))
            .Select(Resolve).Distinct(StringComparer.Ordinal).ToList();
        if (roots.Count == 0) return SteamSetup.None;

        var root = roots[0];
        var libraries = roots.SelectMany(Libraries).Select(Resolve).Distinct(StringComparer.Ordinal).ToList();
        var common = libraries.Select(l => Path.Combine(l, "steamapps", "common")).Where(Directory.Exists).ToList();

        var protons = new List<ProtonInstall>();
        foreach (var folder in common.SelectMany(Directory.EnumerateDirectories))
            if (File.Exists(Path.Combine(folder, "proton"))) protons.Add(Read(folder, fromSteam: true));
        foreach (var tools in roots.Select(r => Path.Combine(r, "compatibilitytools.d")).Append(Path.Combine(home, ".steam", "root", "compatibilitytools.d")).Distinct())
            if (Directory.Exists(tools))
                foreach (var folder in Directory.EnumerateDirectories(tools))
                    if (File.Exists(Path.Combine(folder, "proton"))) protons.Add(Read(folder, fromSteam: false));

        var eac = common.Select(c => Path.Combine(c, "Proton EasyAntiCheat Runtime")).FirstOrDefault(Directory.Exists);
        var container = common.Select(c => Path.Combine(c, "SteamLinuxRuntime_sniper", "_v2-entry-point")).FirstOrDefault(File.Exists);
        return new SteamSetup(root, [.. protons.DistinctBy(p => Resolve(p.Folder)).OrderBy(p => p.Name)], eac, container);
    }

    private static ProtonInstall Read(string folder, bool fromSteam)
    {
        var manifest = Path.Combine(folder, "toolmanifest.vdf");
        var needsRuntime = File.Exists(manifest) && File.ReadAllText(manifest).Contains($"\"{ContainerAppId}\"", StringComparison.Ordinal);
        return new ProtonInstall(Path.GetFileName(folder), folder, needsRuntime, fromSteam);
    }

    /// <summary>Every Steam library folder, from libraryfolders.vdf ("path" "/mnt/games/SteamLibrary").</summary>
    public static IEnumerable<string> Libraries(string root)
    {
        yield return root;
        var file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file)) yield break;
        foreach (Match match in LibraryPath().Matches(File.ReadAllText(file)))
            yield return match.Groups[1].Value.Replace(@"\\", @"\");
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPath();

    /// <summary>Orders Valve's Protons: numbered ones by version, Experimental above them, Hotfix and the rest below.</summary>
    public static double Rank(string name)
    {
        if (name.Contains("Experimental", StringComparison.OrdinalIgnoreCase)) return 1000;
        var match = Regex.Match(name, @"(\d+)\.(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) + int.Parse(match.Groups[2].Value) / 100.0 : 0;
    }

    private static string Resolve(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return Path.TrimEndingDirectorySeparator(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName);
        }
        catch (IOException) { return path; }
    }

    /// <summary>The command line Steam would use: the runtime container if this Proton wants it, then proton itself.</summary>
    public static ProcessStartInfo Command(SteamSetup steam, ProtonInstall proton, string compatData, string game, string exe,
        IReadOnlyList<string> arguments, bool antiCheat)
    {
        var start = new ProcessStartInfo { WorkingDirectory = game, UseShellExecute = false };
        if (proton.NeedsRuntime && steam.ContainerRuntime is { } container)
        {
            start.FileName = container;
            start.ArgumentList.Add("--verb=waitforexitandrun");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(proton.Script);
        }
        else start.FileName = proton.Script;
        start.ArgumentList.Add("waitforexitandrun");
        start.ArgumentList.Add(Path.Combine(game, exe));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        start.Environment["STEAM_COMPAT_DATA_PATH"] = compatData;
        start.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steam.Root ?? "";
        start.Environment["STEAM_COMPAT_INSTALL_PATH"] = game;
        start.Environment["SteamAppId"] = AppId;
        start.Environment["SteamGameId"] = AppId;
        if (antiCheat && steam.AntiCheatRuntime is { } eac) start.Environment["PROTON_EAC_RUNTIME"] = eac;
        return start;
    }
}

/// <summary>How a game process is started on this platform.</summary>
public interface IGameStarter
{
    /// <returns>A process that lives as long as the game does, if the platform gives one.</returns>
    Process? Start(Instance instance, string game, string exe, IReadOnlyList<string> arguments, bool antiCheat);
}

public sealed class WindowsStarter : IGameStarter
{
    public Process? Start(Instance instance, string game, string exe, IReadOnlyList<string> arguments, bool antiCheat)
    {
        var start = new ProcessStartInfo("cmd.exe") { WorkingDirectory = game, UseShellExecute = false, CreateNoWindow = true };
        var command = new System.Text.StringBuilder($"/d /c cd /d \"{game}\" && start \"\" /D \"{game}\" \"{exe}\"");
        foreach (var argument in arguments) command.Append(" \"").Append(argument.Replace("\"", "\\\"")).Append('"');
        start.Arguments = command.ToString();
        start.Environment["SteamAppId"] = Proton.AppId;
        start.Environment["SteamGameId"] = Proton.AppId;
        Process.Start(start);
        return null;
    }
}

/// <param name="setup">Looked up at each start, so a Proton installed while MultiVRC runs is found.</param>
/// <param name="chosen">The Proton folder picked in Settings, or null for the newest.</param>
public sealed class ProtonStarter(LauncherPaths paths, Func<SteamSetup> setup, Func<string?> chosen) : IGameStarter
{
    public Process? Start(Instance instance, string game, string exe, IReadOnlyList<string> arguments, bool antiCheat)
    {
        var steam = setup();
        if (steam.Root is null)
            throw new InvalidOperationException("Steam isn't installed for this user. MultiVRC starts VRChat through Steam's Proton, so install Steam first.");
        if (steam.Pick(chosen()) is not { } proton)
            throw new InvalidOperationException("No Proton found. In Steam, install \"Proton 9.0\" (or newer) from your library's Tools.");
        if (antiCheat && steam.AntiCheatRuntime is null)
            throw new InvalidOperationException("Official VRChat needs Easy Anti-Cheat for Proton. In Steam, install \"Proton EasyAntiCheat Runtime\" from your library's Tools.");
        var compat = paths.InstanceProton(instance.Id);
        Directory.CreateDirectory(compat);
        return Process.Start(Proton.Command(steam, proton, compat, game, exe, arguments, antiCheat));
    }
}

/// <summary>Finds running game processes. Under Wine, Linux sees them by their Windows path (Z:\…\VRChat.exe).</summary>
public static class GameProcesses
{
    public static IEnumerable<int> Find(IEnumerable<string> names)
    {
        var wanted = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            foreach (var name in wanted)
                foreach (var process in Process.GetProcessesByName(name))
                    yield return process.Id;
            yield break;
        }
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            string command;
            try { command = File.ReadAllText(Path.Combine(dir, "cmdline")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (NameOf(command) is { } name && wanted.Contains(name)) yield return pid;
        }
    }

    /// <summary>"Z:\home\me\VRChat.exe\0--no-vr" → "VRChat".</summary>
    public static string? NameOf(string cmdline)
    {
        var first = cmdline.Split('\0')[0];
        if (first.Length == 0) return null;
        var file = first[(first.LastIndexOfAny(['/', '\\']) + 1)..];
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }

    public static void Close(int pid)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.GetProcessById(pid).CloseMainWindow();
            else Unix.kill(pid, Unix.SigTerm);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
    }
}
