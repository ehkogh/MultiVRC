using MultiVRC.Core;

namespace MultiVRC.App.Services;

public sealed class Backdrop(LauncherState state, BackdropPictures pictures)
{
    public string Css { get; private set; } = Default;

    public string Accent { get; private set; } = DefaultAccent;

    public int Version { get; private set; }

    public event Action? Changed;

    public const string Default =
        "radial-gradient(60% 50% at 20% 10%, #1d6b5444, transparent 70%), radial-gradient(50% 60% at 90% 0%, #2a4d8f44, transparent 70%), radial-gradient(60% 50% at 70% 100%, #5a2f8a33, transparent 70%), #0a0c10";

    private const string DefaultAccent = "#6ff0bf";

    private readonly Lock _lock = new();
    private CancellationTokenSource? _slideshow;
    private string? _slideshowKey;
    private string _fallback = Default;
    private readonly Dictionary<string, string> _lastSlide = [];

    public void Set(string css, string accent)
    {
        StopSlideshow();
        Show(css, accent);
    }

    public void Reset() => Set(Default, DefaultAccent);

    public void ShowInstance(Instance instance, string serverCss)
    {
        if (pictures.MigrateOldPicture(instance)) state.Library.SaveInstance(instance);
        if (instance.BackgroundMode == BackgroundMode.Server)
        {
            Set(serverCss, instance.Accent);
            return;
        }

        var key = $"{instance.Id}|{instance.BackgroundMode}|{instance.BackgroundSeconds}|{instance.BackgroundShuffle}|{instance.Accent}";
        string css;
        lock (_lock)
        {
            _fallback = serverCss;
            if (_slideshowKey != key)
            {
                if (_slideshowKey?.StartsWith(instance.Id + "|", StringComparison.Ordinal) == true) _lastSlide.Remove(instance.Id);
                _slideshow?.Cancel();
                var cts = new CancellationTokenSource();
                _slideshow = cts;
                _slideshowKey = key;
                var snapshot = new Snapshot(instance.Id, instance.Accent, instance.BackgroundSeconds, instance.BackgroundShuffle);
                _ = Task.Run(() => RunAsync(snapshot, () => pictures.For(instance), cts.Token));
                css = _lastSlide.GetValueOrDefault(instance.Id) ?? Css;
            }
            else css = _lastSlide.GetValueOrDefault(instance.Id) ?? serverCss;
        }
        Show(css, instance.Accent);
    }

    public void Forget(Instance instance)
    {
        lock (_lock)
        {
            if (_slideshowKey?.StartsWith(instance.Id + "|", StringComparison.Ordinal) == true) _slideshowKey = null;
            _lastSlide.Remove(instance.Id);
        }
    }

    private sealed record Snapshot(string Id, string Accent, int Seconds, bool Shuffle);

    private void StopSlideshow()
    {
        lock (_lock)
        {
            _slideshow?.Cancel();
            _slideshow = null;
            _slideshowKey = null;
        }
    }

    private void Show(string css, string accent)
    {
        lock (_lock)
        {
            if (css == Css && accent == Accent) return;
            Css = css;
            Accent = accent;
            Version++;
        }
        Changed?.Invoke();
    }

    private async Task RunAsync(Snapshot show, Func<List<string>> list, CancellationToken ct)
    {
        var random = new Random();
        var queue = new Queue<string>();
        var delay = TimeSpan.FromSeconds(Math.Max(5, show.Seconds));
        string? last = null;
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            if (queue.Count == 0)
            {
                var all = await Task.Run(list, ct);
                if (all.Count == 0)
                {
                    ShowSlide(show, null, ct);
                    try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { return; }
                    continue;
                }
                if (show.Shuffle) random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(all));
                if (all.Count > 1 && all[0] == last) (all[0], all[^1]) = (all[^1], all[0]);
                foreach (var path in all) queue.Enqueue(path);
            }

            var next = queue.Dequeue();
            var css = await pictures.CssAsync(next);
            if (ct.IsCancellationRequested) return;
            if (css is null) continue;
            last = next;
            if (first && _lastSlide.ContainsKey(show.Id))
            {
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(4, show.Seconds)), ct); } catch (OperationCanceledException) { return; }
            }
            first = false;
            ShowSlide(show, css, ct);
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private void ShowSlide(Snapshot show, string? css, CancellationToken ct)
    {
        string shown;
        lock (_lock)
        {
            if (ct.IsCancellationRequested) return;
            if (css is null) _lastSlide.Remove(show.Id);
            else _lastSlide[show.Id] = css;
            shown = css ?? _fallback;
        }
        Show(shown, show.Accent);
    }
}
