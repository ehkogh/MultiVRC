using MultiVRC.Core;

namespace MultiVRC.App.Services;

public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly LauncherState _state;
    private readonly Toasts _toasts;
    private readonly Tray _tray;
    private readonly HashSet<string> _announced = [];
    private readonly Updates _updates;
    private readonly Dictionary<string, PendingUpdate> _pending = [];
    private readonly SemaphoreSlim _applying = new(1, 1);
    private Timer? _timer;

    public (string InstanceId, string Status, double Percent)? Active { get; private set; }

    public bool Checking { get; private set; }

    public event Action? Changed;

    public UpdateService(LauncherState state, Toasts toasts, Tray tray)
    {
        _state = state;
        _toasts = toasts;
        _tray = tray;
        _updates = new Updates(state.Library, state.Branches);
    }

    public DateTimeOffset? LastChecked => _updates.LastChecked;
    public IReadOnlyList<string> Problems => _updates.Problems;

    public PendingUpdate? PendingFor(string instanceId) => _pending.GetValueOrDefault(instanceId);

    public void Start() => _timer ??= new Timer(_ => _ = CheckAsync(auto: true), null, TimeSpan.FromSeconds(3), Interval);

    public async Task CheckAsync(bool auto = true)
    {
        if (Checking) return;
        Checking = true;
        Changed?.Invoke();
        try
        {
            var found = await _updates.CheckAsync();
            lock (_pending)
            {
                _pending.Clear();
                foreach (var update in found) _pending[update.InstanceId] = update;
            }
        }
        finally
        {
            Checking = false;
            Changed?.Invoke();
        }

        Announce(auto);
        if (!auto || !_state.Settings.AutoUpdate) return;
        foreach (var update in _pending.Values.ToList())
            if (_state.Library.FindInstance(update.InstanceId) is { AutoUpdate: true } instance && _state.Runner.Running?.Id != instance.Id)
                await ApplyAsync(update.InstanceId, quiet: true);
    }

    private bool WillApply(Instance instance, bool auto) =>
        auto && _state.Settings.AutoUpdate && instance.AutoUpdate && _state.Runner.Running?.Id != instance.Id;

    /// <summary>Tells about each update once; the ones about to install themselves are told about as they do.</summary>
    private void Announce(bool auto)
    {
        foreach (var update in _pending.Values.ToList())
        {
            if (!_announced.Add($"{update.InstanceId}|{update.Summary}")) continue;
            if (_state.Library.FindInstance(update.InstanceId) is not { } instance || WillApply(instance, auto)) continue;
            _tray.Notify(NotifyKind.UpdateAvailable, $"{instance.Name} has an update", $"{update.Summary}. Click to open it.", $"/instance/{instance.Id}");
        }
    }

    public async Task ApplyAsync(string instanceId, bool quiet = false)
    {
        if (PendingFor(instanceId) is not { } update || _state.Library.FindInstance(instanceId) is not { } instance) return;
        if (_state.Runner.Running?.Id == instanceId) { if (!quiet) _toasts.Error($"{instance.Name} is running; it updates once it closes."); return; }
        await _applying.WaitAsync();
        try
        {
            Report(instanceId, "Starting…", 0);
            _tray.Notify(NotifyKind.UpdateStarted, $"Updating {instance.Name}", update.Summary, $"/instance/{instanceId}");
            var template = instance.TemplateId is { } id ? _state.Library.FindTemplate(id) : null;
            var buildId = instance.BuildId;
            if (update.ManifestId is { } manifest)
                buildId = (_state.Library.FindBuildByManifest(manifest) ?? await GetBuildAsync(instance, update)).Id;
            Report(instanceId, "Setting up…", 100);
            await _state.Library.SwitchBuildAsync(instance, buildId, template);
            lock (_pending) _pending.Remove(instanceId);
            var done = $"{instance.Name} is up to date{(update.SteamBuildId is { } b ? $" (build {b})" : update.TemplateVersion is { Length: > 0 } v ? $" ({v})" : "")}.";
            _toasts.Ok(done);
            _tray.Notify(NotifyKind.UpdateFinished, $"{instance.Name} updated", done, $"/instance/{instanceId}");
            _state.NotifyChanged();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or HttpRequestException or UnauthorizedAccessException)
        {
            _toasts.Error($"{instance.Name} could not update: {e.Message}");
            _tray.Notify(NotifyKind.UpdateFailed, $"{instance.Name} didn't update", e.Message, $"/instance/{instanceId}");
        }
        finally
        {
            Active = null;
            _applying.Release();
            Changed?.Invoke();
        }
    }

    private async Task<Build> GetBuildAsync(Instance instance, PendingUpdate update)
    {
        var name = update.Branch is { } branch ? $"VRChat ({Updates.Label(branch)}) {update.SteamBuildId}" : $"VRChat {update.ManifestId}";
        if (_state.Demo) return await DemoData.FakeDownloadAsync(_state.Paths, _state.Library, update, name, p => Report(instance.Id, "Downloading", p));
        if (!await _state.Steam.ResumeAsync())
            throw new InvalidOperationException("Sign in to Steam on the Builds page and it will finish updating.");
        var download = _state.StartDownload(new DepotRequest(update.ManifestId!, update.Branch ?? "public", name, "", update.SteamBuildId));
        download.Changed += () => Report(instance.Id, "Downloading", download.Percent);
        return await download.Done ?? throw new InvalidOperationException(download.Error ?? "The download did not finish.");
    }

    private void Report(string instanceId, string status, double percent)
    {
        Active = (instanceId, status, Math.Clamp(percent, 0, 100));
        Changed?.Invoke();
    }

    public void Dispose() => _timer?.Dispose();
}
