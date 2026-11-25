using MultiVRC.Core;

namespace MultiVRC.App.Services;

public sealed record LaunchOptions(bool Demo, string DataDir)
{
    public static LaunchOptions FromArgs(string[] args)
    {
#if DEBUG
        var demo = !args.Contains("--real");
#else
        var demo = args.Contains("--demo");
#endif
        return new LaunchOptions(demo, demo ? DemoData.Folder : LauncherPaths.Default.Root);
    }
}

public static class DemoData
{
    internal static string ReleaseManifest = "";

    public static string VerifiedListUrl(LauncherPaths paths) => new Uri(Path.Combine(paths.Root, "demo-server", "verified-servers.json")).AbsoluteUri;

    private const string NeonIcon = "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='%23ff4fa3'/><stop offset='1' stop-color='%236a4cff'/></linearGradient></defs><rect width='64' height='64' rx='14' fill='%230d0614'/><path d='M14 46V18l18 20V18M38 18h12M44 18v28' stroke='url(%23g)' stroke-width='5' fill='none' stroke-linecap='round' stroke-linejoin='round'/></svg>";

    public static async Task<Build> FakeDownloadAsync(LauncherPaths paths, Library library, PendingUpdate update, string name, Action<double> progress)
    {
        for (var percent = 0; percent <= 100; percent += 4)
        {
            progress(percent);
            await Task.Delay(220);
        }
        var build = new Build
        {
            Id = JsonFiles.NewId(), Name = name, Version = update.Branch == "open-beta" ? "2026.5.0-beta.4" : "2026.4.2",
            ManifestId = update.ManifestId, Branch = update.Branch, SteamBuildId = update.SteamBuildId, Source = "steam",
            SizeBytes = 11_200_000_000, AddedAt = DateTimeOffset.UtcNow,
        };
        Directory.CreateDirectory(paths.BuildFiles(build.Id));
        File.WriteAllText(Path.Combine(paths.BuildFiles(build.Id), "VRChat.exe"), "demo");
        library.SaveBuild(build);
        return build;
    }
    public static string Folder => Path.Combine(Path.GetTempPath(), "MultiVRC-demo");

    public static void Seed(LauncherPaths paths, Library library)
    {
        if (Directory.Exists(paths.Root)) FileLinks.DeleteTree(paths.Root);
        paths.EnsureCreated();
        var now = DateTimeOffset.UtcNow;

        Build Build(string id, string name, string version, string branch, long gb, int daysAgo)
        {
            var build = new Build
            {
                Id = id, Name = name, Version = version, Branch = branch, Source = "steam",
                ManifestId = $"{4823409823409820000 + gb}", SizeBytes = gb * 1_000_000_000, AddedAt = now.AddDays(-daysAgo),
            };
            Directory.CreateDirectory(paths.BuildFiles(id));
            File.WriteAllText(Path.Combine(paths.BuildFiles(id), "VRChat.exe"), "demo");
            library.SaveBuild(build);
            return build;
        }
        Build("release", "VRChat 2026.4", "2026.4.1p1-1937", "public", 10, 2);
        Build("beta", "Open beta", "2026.5.0-beta.3", "open-beta", 11, 1);
        ReleaseManifest = library.FindBuild("release")!.ManifestId!;
        Build("legacy", "VRChat 2022.3", "2022.3.22f1", "public", 7, 40);

        string NewsFile(string name, params (string Title, string Summary, int DaysAgo)[] posts)
        {
            var file = Path.Combine(paths.Root, "demo-news", $"{name}.json");
            JsonFiles.Write(file, posts.Select(p => new NewsItem(p.Title, p.Summary, null, "https://example.com/news", now.AddDays(-p.DaysAgo))).ToList());
            return new Uri(file).AbsoluteUri;
        }

        var neon = new Template
        {
            Id = "neon-district", Name = "Neon District", Server = "Neon District", Kind = InstanceKind.Modded, Accent = "#e06c9f",
            Version = "1.3", Description = "A made-up private server, for the demo.",
            Build = new TemplateBuild { Version = "2026.4.1p1-1937" },
            PlayModes =
            [
                new TemplatePlayMode { Label = "Play in desktop mode", Icon = "monitor", Arguments = ["--no-vr"] },
                new TemplatePlayMode { Label = "Play with the debug console", Icon = "terminal", Arguments = ["--enable-debug-gui", "--log-debug-levels"] },
                new TemplatePlayMode { Label = "Play without mods", Icon = "shield-off", Executable = "VRChat.exe" },
            ],
            Branding = new Branding
            {
                Tagline = "Nightlife, rooftops and too many neon signs",
                IconUrl = NeonIcon,
                News = new NewsSource
                {
                    Type = "json",
                    Url = NewsFile("neon",
                        ("Season 3 is live", "New districts, a rebuilt club and a rooftop race every Friday night. Everyone gets the season badge on their first visit.", 1),
                        ("Maintenance on Saturday", "The servers go down for about an hour at 10:00 UTC while we move to new hardware.", 4),
                        ("New world: Rooftop Garden", "A quiet place above the noise, with a koi pond and a very patient cat.", 9),
                        ("Patch notes 1.4", "Faster joins, fixed mirror flicker, and the vending machines take credits again.", 15)),
                },
                Links =
                [
                    new BrandLink { Label = "Discord", Url = "https://example.com/discord", Kind = "discord" },
                    new BrandLink { Label = "Website", Url = "https://example.com", Kind = "website" },
                ],
            },
        };
        var published = Path.Combine(paths.Root, "demo-server", "neon-district.json");
        neon.SourceUrl = new Uri(published).AbsoluteUri;
        library.SaveTemplate(neon);
        neon.Version = "1.4";
        JsonFiles.Write(published, neon);
        var starlight = new Template
        {
            Id = "starlight-plaza", Name = "Starlight Plaza", Server = "Starlight Plaza", Kind = InstanceKind.Modded, Accent = "#4fc1d6",
            Version = "2.0", Description = "A calm hangout server with stargazing worlds and an open mic night.",
            Build = new TemplateBuild { Version = "2026.4.1p1-1937" },
            Branding = new Branding { Tagline = "Stargazing, open mics and late-night chats" },
        };
        var starlightUrl = Path.Combine(paths.Root, "demo-server", "starlight-plaza.json");
        JsonFiles.Write(starlightUrl, starlight);
        JsonFiles.Write(Path.Combine(paths.Root, "demo-server", "verified-servers.json"), new List<VerifiedServer>
        {
            new() { Name = "Neon District", TemplateUrl = neon.SourceUrl, VerifiedBy = ["kiri", "Mochi", "the_archivist"], Note = "Its plugins were reviewed and its downloads are pinned by checksum." },
            new() { Name = "Starlight Plaza", TemplateUrl = new Uri(starlightUrl).AbsoluteUri, VerifiedBy = ["kiri"], Note = "A calm hangout server with stargazing worlds." },
        });
        library.SaveTemplate(new Template
        {
            Id = "photon-local", Name = "Photon Local", Server = "Photon local", Kind = InstanceKind.Modded, Accent = "#9b7bf0",
            Description = "A private server running on this PC.",
            Build = new TemplateBuild { Version = "2026.4.1p1-1937" },
            Branding = new Branding
            {
                Tagline = "Our own VRChat server, running on this PC",
                News = new NewsSource
                {
                    Type = "json",
                    Url = NewsFile("photon",
                        ("Purchases unlock mid-session", "Buying something in a world now unlocks it within seconds, no rejoin needed.", 0),
                        ("The admin panel got tabs", "Every page fits on a screen now. The world editor keeps subscriptions as they are.", 0)),
                },
                Links = [new BrandLink { Label = "GitHub", Url = "https://github.com/ehkogh/Kumo", Kind = "github" }],
            },
        });

        var order = 0;
        void Instance(string id, string name, string build, string? template, InstanceKind kind, string server, string accent,
            double hoursAgo, int playedHours)
        {
            var instance = new Instance
            {
                Id = id, Name = name, BuildId = build, TemplateId = template, Kind = kind, Server = server, Accent = accent, ArtSeed = $"demo-{id}",
                TrackBranch = kind == InstanceKind.Official ? library.FindBuild(build)?.Branch : null,
                TemplateVersion = template is null ? null : "1.3",
                AutoUpdate = id != "beta",
                SortOrder = order, CreatedAt = now.AddDays(-30 + order++), LastPlayedAt = hoursAgo < 0 ? null : now.AddHours(-hoursAgo),
                TotalPlayed = TimeSpan.FromHours(playedHours) + TimeSpan.FromMinutes(id.Length * 7),
            };
            Directory.CreateDirectory(paths.InstanceGame(id));
            Directory.CreateDirectory(paths.InstanceData(id));
            File.WriteAllText(Path.Combine(paths.InstanceGame(id), "VRChat.exe"), "demo");
            library.SaveInstance(instance);
        }
        Instance("vrchat", "VRChat", "release", "vrchat-official", InstanceKind.Official, "VRChat", "#3fb68b", 0.2, 132);
        Instance("neon", "Neon District", "release", "neon-district", InstanceKind.Modded, "Neon District", "#e06c9f", 5, 61);
        Instance("photon", "Photon Local", "release", "photon-local", InstanceKind.Modded, "Photon local", "#9b7bf0", 26, 47);
        Instance("legacy", "Legacy 2022", "legacy", null, InstanceKind.Modded, "shoya", "#e0a84a", 400, 9);
        Instance("beta", "Open Beta", "beta", "vrchat-official", InstanceKind.Official, "VRChat", "#5b9be0", -1, 0);
    }
}

public sealed class DemoBranches : IBranchSource
{
    public Task<IReadOnlyList<SteamBranch>> GetAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SteamBranch>>(
    [
        new SteamBranch("public", "", 26551832, DemoData.ReleaseManifest, DateTimeOffset.UtcNow.AddDays(-2), false),
        new SteamBranch("open-beta", "Open Beta", 26560001, "7468092464173029999", DateTimeOffset.UtcNow.AddHours(-3), false),
    ]);
}

public sealed class DemoSteam : ISteamAccount
{
    public string? AccountName { get; private set; }
    public bool SignedIn { get; private set; }
    public SteamProfile? Profile { get; private set; }
    public bool HasSavedLogin => false;
    public event Action? Changed;

    public Task<bool> ResumeAsync(CancellationToken ct = default) => Task.FromResult(SignedIn);

    public async Task SignInAsync(string username, string password, SteamKit2.Authentication.IAuthenticator guard, CancellationToken ct = default)
    {
        await Task.Delay(700, ct);
        if (password.Length == 0) throw new InvalidOperationException("Steam refused the sign-in (InvalidPassword).");
        await guard.GetDeviceCodeAsync(false);
        await Task.Delay(500, ct);
        Finish(username);
    }

    public async Task SignInWithQrAsync(Action<string> showUrl, CancellationToken ct = default)
    {
        while (true)
        {
            showUrl($"https://s.team/q/1/{Guid.NewGuid():N}");
            await Task.Delay(15000, ct);
        }
    }

    private void Finish(string name)
    {
        AccountName = name;
        Profile = new SteamProfile(char.ToUpperInvariant(name[0]) + name[1..], null, DateTimeOffset.UtcNow);
        SignedIn = true;
        Changed?.Invoke();
    }

    public void SignOut()
    {
        AccountName = null;
        Profile = null;
        SignedIn = false;
        Changed?.Invoke();
    }

    public async Task<Build> DownloadAsync(DepotRequest request, Library library, IProgress<DownloadProgress> progress, CancellationToken ct = default)
    {
        if (library.FindBuildByManifest(request.ManifestId) is { } existing) return existing;
        var update = new PendingUpdate("", UpdateKind.SteamBuild, "", request.ManifestId, request.Branch, request.SteamBuildId, null);
        return await DemoData.FakeDownloadAsync(library.Paths, library, update, request.Name,
            p => progress.Report(new DownloadProgress(p, $"{p * 0.112:0.00} GB of 11.2 GB")));
    }
}
