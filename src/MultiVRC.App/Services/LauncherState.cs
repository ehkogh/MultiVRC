using MultiVRC.Core;

namespace MultiVRC.App.Services;

public sealed class Settings
{
    public bool MinimizeOnLaunch { get; set; } = true;

    public bool AutoUpdate { get; set; } = true;

    public bool UpdateLauncher { get; set; } = true;

    public bool LauncherPrereleases { get; set; }
}

public sealed class LauncherState : IDisposable
{
    public LauncherPaths Paths { get; }
    public Library Library { get; }
    public Isolation Isolation { get; }
    public GameRunner Runner { get; }
    public ISteamAccount Steam { get; }
    public NewsReader News { get; }

    public Verification Verification { get; }

    public IBranchSource Branches { get; }

    public static string Version { get; } =
        (System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];
    public Settings Settings { get; private set; }

    public string? StartupNotice { get; }

    public ActiveDownload? CurrentDownload { get; private set; }

    public ActiveDownload StartDownload(DepotRequest request)
    {
        if (CurrentDownload is { Finished: false }) throw new InvalidOperationException($"{CurrentDownload.Name} is still downloading.");
        var download = ActiveDownload.Start(Steam, request, Library);
        download.Changed += () => Changed?.Invoke();
        CurrentDownload = download;
        Changed?.Invoke();
        return download;
    }

    public void DismissDownload()
    {
        if (CurrentDownload is { Finished: true }) CurrentDownload = null;
        Changed?.Invoke();
    }

    public event Action? Changed;

    public bool Demo { get; }

    public LauncherState(HttpClient http, LaunchOptions options)
    {
        Demo = options.Demo;
        Paths = new LauncherPaths(options.DataDir);
        Paths.EnsureCreated();
        Library = new Library(Paths, http);
        if (Demo) DemoData.Seed(Paths, Library);
        Isolation = new Isolation(Paths);
        Runner = new GameRunner(Library, Isolation, simulate: Demo);
        Steam = Demo ? new DemoSteam() : new SteamSession(Paths, http);
        Steam.Changed += () => Changed?.Invoke();
        News = new NewsReader(Paths, http);
        Branches = Demo ? new DemoBranches() : new SteamBranches();
        Verification = new Verification(Paths, http, Demo ? DemoData.VerifiedListUrl(Paths) : Environment.GetEnvironmentVariable("MULTIVRC_VERIFIED_URL") is { Length: > 0 } listUrl ? listUrl : Verification.DefaultListUrl);
        _ = Task.Run(async () => { await Verification.RefreshAsync(); Changed?.Invoke(); });
        Settings = JsonFiles.Read<Settings>(Paths.SettingsFile) ?? new Settings();
        Runner.Changed += () => Changed?.Invoke();
        BuiltInTemplates.Install(Library);
        try { StartupNotice = Runner.RecoverAfterCrash(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StartupNotice = $"The last session's data could not be put back automatically: {e.Message}. Your own VRChat data is in {LauncherPaths.GameDataFolder}.multivrc-original.";
        }
    }

    public void SaveSettings(Settings settings)
    {
        Settings = settings;
        JsonFiles.Write(Paths.SettingsFile, settings);
    }

    public void NotifyChanged() => Changed?.Invoke();

    public void Dispose()
    {
        Runner.Dispose();
        (Steam as IDisposable)?.Dispose();
    }
}

public static class BuiltInTemplates
{
    public static void Install(Library library)
    {
        library.SaveTemplate(new Template
        {
            Id = "vrchat-official", Name = "VRChat", Server = "VRChat", Kind = InstanceKind.Official,
            Description = "The game exactly as Steam ships it, signing in to VRChat. Its files are never changed and nothing is loaded into it.",
            Accent = "#3fb68b", ShareCache = true,
            Build = new TemplateBuild { Branch = "public" },
            Branding = new Branding
            {
                Tagline = "The official VRChat, untouched",
                IconUrl = "https://assets.vrchat.com/www/favicons/apple-touch-icon-180x180.png",
                IconBackground = "#ffffff",
                News = new NewsSource { Type = "rss", Url = "https://hello.vrchat.com/blog?format=rss" },
                Links =
                [
                    new BrandLink { Label = "Website", Url = "https://hello.vrchat.com", Kind = "website" },
                    new BrandLink { Label = "Discord", Url = "https://discord.gg/vrchat", Kind = "discord" },
                    new BrandLink { Label = "X", Url = "https://x.com/VRChat", Kind = "x" },
                    new BrandLink { Label = "YouTube", Url = "https://www.youtube.com/@VRChat", Kind = "youtube" },
                ],
            },
        });
    }
}
