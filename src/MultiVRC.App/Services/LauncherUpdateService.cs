using System.Diagnostics;
using MultiVRC.Core;

namespace MultiVRC.App.Services;

public sealed class LauncherUpdateService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(4);

    private readonly LauncherState _state;
    private readonly Toasts _toasts;
    private readonly LauncherUpdater? _updater;
    private Timer? _timer;

    public enum Phase { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }

    public Phase Status { get; private set; } = Phase.Idle;
    public LauncherRelease? Latest { get; private set; }
    public double Percent { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? LastChecked { get; private set; }

    public bool Supported => _updater is not null;

    public string? ReadyVersion => _state.Demo ? (Status == Phase.Ready ? Latest?.Version : null) : _updater?.ReadyVersion;

    public event Action? Changed;

    public LauncherUpdateService(LauncherState state, Toasts toasts, HttpClient http)
    {
        _state = state;
        _toasts = toasts;
        IReleaseSource? source = state.Demo ? new DemoReleases() : IsReleaseBuild ? new GitHubReleases(http) : null;
        if (source is not null)
            _updater = new LauncherUpdater(state.Paths, http, source, LauncherState.Version, AppContext.BaseDirectory);
        if (ReadyVersion is not null) Status = Phase.Ready;
    }

#if DEBUG
    private static bool IsReleaseBuild => false;
#else
    private static bool IsReleaseBuild => true;
#endif

    public void Start() => _timer ??= new Timer(_ => _ = CheckAsync(manual: false), null, TimeSpan.FromSeconds(8), Interval);

    public async Task CheckAsync(bool manual = true)
    {
        if (_updater is null || Status is Phase.Checking or Phase.Downloading) return;
        if (!manual && !_state.Settings.UpdateLauncher && Status == Phase.Ready) return;
        Set(Phase.Checking);
        try
        {
            Latest = await _updater.CheckAsync(_state.Settings.LauncherPrereleases);
            LastChecked = DateTimeOffset.Now;
            if (Latest is null) { Set(ReadyVersion is null ? Phase.UpToDate : Phase.Ready); return; }
            if (ReadyVersion == Latest.Version) { Set(Phase.Ready); return; }
            Set(Phase.Available);
            if (_state.Settings.UpdateLauncher) await DownloadAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Fail(manual ? $"Couldn't check for a new MultiVRC: {e.Message}" : null, e.Message);
        }
    }

    public async Task DownloadAsync()
    {
        if (_updater is null || Latest is not { } release || Status == Phase.Downloading) return;
        Percent = 0;
        Set(Phase.Downloading);
        try
        {
            var progress = new Progress<double>(p => { Percent = p; Changed?.Invoke(); });
            if (_state.Demo) await DemoReleases.FakeDownloadAsync(progress);
            else await _updater.DownloadAsync(release, progress);
            Set(Phase.Ready);
            _toasts.Ok($"MultiVRC {release.Version} is ready. It installs the next time MultiVRC starts, or now from Settings.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or InvalidDataException)
        {
            Fail($"MultiVRC {release.Version} didn't download: {e.Message}", e.Message);
        }
    }

    public void InstallAndRestart()
    {
        if (_state.Runner.Running is not null)
        {
            _toasts.Error("Close the game first: MultiVRC is looking after its session.");
            return;
        }
        if (_state.Demo)
        {
            _toasts.Ok($"Demo: MultiVRC would restart as {Latest?.Version} now.");
            return;
        }
        try
        {
            var exe = _updater!.Install();
            SingleInstance.Current?.Dispose();
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
            WindowActions.Close();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Fail($"MultiVRC couldn't update itself: {e.Message}", e.Message);
        }
    }

    public static bool InstallOnStart(LaunchOptions options, string[] args)
    {
        if (!IsReleaseBuild) return false;
        LauncherUpdater.RemoveLeftovers(AppContext.BaseDirectory);
        if (options.Demo) return false;
        var paths = new LauncherPaths(options.DataDir);
        var settings = JsonFiles.Read<Settings>(paths.SettingsFile) ?? new Settings();
        if (!settings.UpdateLauncher) return false;
        var updater = new LauncherUpdater(paths, new HttpClient(), new NoReleases(), LauncherState.Version, AppContext.BaseDirectory);
        if (updater.ReadyVersion is null) return false;
        try
        {
            var exe = updater.Install();
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            Process.Start(start);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private sealed class NoReleases : IReleaseSource
    {
        public Task<IReadOnlyList<LauncherRelease>> GetAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LauncherRelease>>([]);
    }

    private void Set(Phase phase)
    {
        Status = phase;
        if (phase != Phase.Failed) Error = null;
        Changed?.Invoke();
    }

    private void Fail(string? toast, string error)
    {
        Error = error;
        Status = Phase.Failed;
        if (toast is not null) _toasts.Error(toast);
        Changed?.Invoke();
    }

    public void Dispose() => _timer?.Dispose();
}

public sealed class DemoReleases : IReleaseSource
{
    public Task<IReadOnlyList<LauncherRelease>> GetAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LauncherRelease>>(
        [
            new LauncherRelease("0.2.0", "Settings on one page, Steam profile pictures, and launch options for official instances.",
                "https://example.com/MultiVRC-win-x64.zip", null, false, DateTimeOffset.Now.AddDays(-1), null),
            new LauncherRelease("0.3.0-beta.1", "Try the next version early.", "https://example.com/MultiVRC-win-x64.zip", null, true, DateTimeOffset.Now, null),
        ]);

    public static async Task FakeDownloadAsync(IProgress<double> progress)
    {
        for (var p = 0; p <= 100; p += 5)
        {
            progress.Report(p);
            await Task.Delay(90);
        }
    }
}
