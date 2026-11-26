using System.Diagnostics;

namespace MultiVRC.Core;

public enum LaunchMode
{
    Normal,

    Desktop,

    WithoutAntiCheat,
}

public sealed class GameRunner(Library library, IIsolation isolation, bool simulate = false, IGameStarter? starter = null) : IDisposable
{
    private static readonly TimeSpan SimulatedSession = TimeSpan.FromSeconds(20);

    private static readonly string[] GameProcessNames = ["VRChat", "VRChatML", "start_protected_game"];

    private Timer? _watch;
    private DateTimeOffset _startedAt;
    private string? _extraProcessName;
    private Process? _launched;
    private readonly IGameStarter _starter = starter ?? new WindowsStarter();

    public Instance? Running { get; private set; }

    public event Action? Changed;

    private IEnumerable<string> Names => GameProcessNames.Append(_extraProcessName).OfType<string>();

    public bool AnyGameRunning() => !simulate && (_launched is { HasExited: false } || GameProcesses.Find(Names).Any());

    public string? RecoverAfterCrash()
    {
        if (simulate || isolation.Current is not { } swap) return null;
        if (AnyGameRunning())
        {
            if (library.FindInstance(swap.InstanceId) is not { } instance) return null;
            Running = instance;
            _startedAt = swap.StartedAt;
            _extraProcessName = Path.GetFileNameWithoutExtension(instance.Executable);
            _watch = new Timer(_ => Check(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            return $"{instance.Name} is still running; its data goes back when it closes.";
        }
        var id = isolation.Recover(AnyGameRunning);
        return id is null ? null : $"The last session ({library.FindInstance(id)?.Name ?? id}) ended while the launcher was closed; your own VRChat data is back in place.";
    }

    public void Launch(Instance instance, LaunchMode mode = LaunchMode.Normal, TemplatePlayMode? play = null)
    {
        var official = instance.Kind == InstanceKind.Official;
        if (!official) mode = LaunchMode.Normal;
        if (official && play?.Executable is { Length: > 0 })
            throw new InvalidOperationException($"\"{play.Label}\" starts another program, which an official instance never does (it always starts through Easy Anti-Cheat).");
        if (Running is not null) throw new InvalidOperationException($"{Running.Name} is already running. One instance at a time.");
        if (AnyGameRunning())
            throw new InvalidOperationException("VRChat is already running outside the launcher. Close it first: an instance's data cannot be swapped in while it runs.");
        if (simulate)
        {
            Running = instance;
            _startedAt = DateTimeOffset.UtcNow;
            instance.LastPlayedAt = _startedAt;
            library.SaveInstance(instance);
            _watch = new Timer(_ => End(), null, SimulatedSession, Timeout.InfiniteTimeSpan);
            Changed?.Invoke();
            return;
        }

        var game = library.Paths.InstanceGame(instance.Id);
        var exe = official && mode != LaunchMode.WithoutAntiCheat && File.Exists(Path.Combine(game, "start_protected_game.exe"))
            ? "start_protected_game.exe"
            : play?.Executable is { Length: > 0 } other ? Path.GetRelativePath(game, Library.InsideGame(game, other)) : instance.Executable;
        var arguments = instance.Arguments.ToList();
        foreach (var argument in play?.Arguments ?? [])
            if (!arguments.Contains(argument, StringComparer.OrdinalIgnoreCase)) arguments.Add(argument);
        if (mode == LaunchMode.Desktop && !arguments.Contains("--no-vr", StringComparer.OrdinalIgnoreCase)) arguments.Add("--no-vr");
        var path = Path.Combine(game, exe);
        if (!File.Exists(path)) throw new InvalidOperationException($"{exe} is missing from the instance's game folder.");

        isolation.Apply(instance);
        try
        {
            _launched = _starter.Start(instance, game, exe, arguments, antiCheat: exe == "start_protected_game.exe");
        }
        catch
        {
            isolation.Restore();
            throw;
        }

        Running = instance;
        _startedAt = DateTimeOffset.UtcNow;
        _extraProcessName = Path.GetFileNameWithoutExtension(instance.Executable);
        instance.LastPlayedAt = _startedAt;
        library.SaveInstance(instance);
        _watch = new Timer(_ => Check(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(2));
        Changed?.Invoke();
    }

    private void Check()
    {
        if (Running is null || AnyGameRunning()) return;
        End();
    }

    private void End()
    {
        if (Running is not { } instance) return;
        _watch?.Dispose();
        _watch = null;
        _launched?.Dispose();
        _launched = null;
        try
        {
            if (!simulate) isolation.Restore();
        }
        finally
        {
            if (library.FindInstance(instance.Id) is { } saved)
            {
                saved.TotalPlayed += DateTimeOffset.UtcNow - _startedAt;
                library.SaveInstance(saved);
            }
            Running = null;
            _extraProcessName = null;
            Changed?.Invoke();
        }
    }

    public void Stop()
    {
        if (simulate) { End(); return; }
        foreach (var pid in GameProcesses.Find(Names).ToList()) GameProcesses.Close(pid);
    }

    public void Dispose() => _watch?.Dispose();
}
