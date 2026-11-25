namespace MultiVRC.App.Services;

public sealed class Toasts
{
    public sealed record Toast(long Id, string Message, bool IsError);

    private readonly List<Toast> _shown = [];
    private long _next;

    public IReadOnlyList<Toast> Shown { get { lock (_shown) return [.. _shown]; } }

    public event Action? Changed;

    public void Ok(string message) => Show(message, false);

    public void Error(string message) => Show(message, true);

    private void Show(string message, bool isError)
    {
        var toast = new Toast(Interlocked.Increment(ref _next), message, isError);
        lock (_shown)
        {
            _shown.Add(toast);
            if (_shown.Count > 4) _shown.RemoveAt(0);
        }
        Changed?.Invoke();
        if (!isError) _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => Dismiss(toast.Id), TaskScheduler.Default);
    }

    public void Dismiss(long id)
    {
        lock (_shown) _shown.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }

    public async Task<bool> TryAsync(Func<Task> action, string? success = null)
    {
        try
        {
            await action();
            if (success is not null) Ok(success);
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException
                                       or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or ArgumentException)
        {
            Error(e.Message);
            return false;
        }
    }

    public Task<bool> Try(Action action, string? success = null) => TryAsync(() => { action(); return Task.CompletedTask; }, success);
}
