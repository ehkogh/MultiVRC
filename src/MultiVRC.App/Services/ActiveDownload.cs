using MultiVRC.Core;

namespace MultiVRC.App.Services;

public sealed class ActiveDownload
{
    private readonly CancellationTokenSource _cancel = new();

    public required string Name { get; init; }
    public double Percent { get; private set; }
    public string Status { get; private set; } = "Starting…";
    public bool Finished { get; private set; }
    public Build? Result { get; private set; }
    public string? Error { get; private set; }
    public Task<Build?> Done { get; private set; } = Task.FromResult<Build?>(null);

    public event Action? Changed;

    public static ActiveDownload Start(ISteamAccount steam, DepotRequest request, Library library)
    {
        var download = new ActiveDownload { Name = request.Name };
        var progress = new Progress<DownloadProgress>(p =>
        {
            download.Percent = p.Percent;
            download.Status = p.Status;
            download.Changed?.Invoke();
        });
        download.Done = Task.Run(async () =>
        {
            try
            {
                download.Result = await steam.DownloadAsync(request, library, progress, download._cancel.Token);
                return download.Result;
            }
            catch (OperationCanceledException)
            {
                download.Error = "Cancelled.";
                return null;
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or HttpRequestException or TimeoutException or UnauthorizedAccessException)
            {
                download.Error = e.Message;
                return null;
            }
            finally
            {
                download.Finished = true;
                download.Changed?.Invoke();
            }
        });
        return download;
    }

    public void Cancel() => _cancel.Cancel();
}
