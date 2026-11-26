using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;

namespace MultiVRC.Core;

public sealed record DepotRequest(string ManifestId, string Branch, string Name, string Version, long? SteamBuildId);

public sealed record DownloadProgress(double Percent, string Status);

public interface ISteamAccount
{
    string? AccountName { get; }
    bool SignedIn { get; }

    SteamProfile? Profile { get; }

    bool HasSavedLogin { get; }

    event Action? Changed;

    Task<bool> ResumeAsync(CancellationToken ct = default);

    Task SignInAsync(string username, string password, IAuthenticator guard, CancellationToken ct = default);

    Task SignInWithQrAsync(Action<string> showUrl, CancellationToken ct = default);

    void SignOut();

    Task<Build> DownloadAsync(DepotRequest request, Library library, IProgress<DownloadProgress> progress, CancellationToken ct = default);
}

public sealed class SteamSession : ISteamAccount, IDisposable
{
    private const uint AppId = 438100, DepotId = 438101;
    private const int Parallel = 8, Attempts = 5;

    private readonly LauncherPaths _paths;
    private readonly SteamClient _client = new();
    private readonly CallbackManager _manager;
    private readonly SteamUser _user;
    private readonly SteamApps _apps;
    private readonly SteamContent _content;
    private readonly SemaphoreSlim _connecting = new(1, 1);
    private Thread? _pump;
    private volatile bool _running;
    private TaskCompletionSource? _connected;
    private TaskCompletionSource<EResult>? _loggedOn;
    private readonly HttpClient? _http;

    public string? AccountName { get; private set; }
    public bool SignedIn { get; private set; }
    public SteamProfile? Profile { get; private set; }
    public bool HasSavedLogin => File.Exists(TokenFile);
    public event Action? Changed;

    private string TokenFile => Path.Combine(_paths.Root, "steam-login.bin");

    public SteamSession(LauncherPaths paths, HttpClient? http = null)
    {
        _paths = paths;
        _http = http;
        _manager = new CallbackManager(_client);
        _user = _client.GetHandler<SteamUser>()!;
        _apps = _client.GetHandler<SteamApps>()!;
        _content = _client.GetHandler<SteamContent>()!;
        _manager.Subscribe<SteamClient.ConnectedCallback>(_ => _connected?.TrySetResult());
        _manager.Subscribe<SteamClient.DisconnectedCallback>(_ =>
        {
            _connected?.TrySetException(new InvalidOperationException("Could not reach Steam."));
            _loggedOn?.TrySetResult(EResult.NoConnection);
            if (SignedIn) { SignedIn = false; Changed?.Invoke(); }
        });
        _manager.Subscribe<SteamUser.LoggedOnCallback>(callback => _loggedOn?.TrySetResult(callback.Result));
        _manager.Subscribe<SteamUser.LoggedOffCallback>(_ => { SignedIn = false; Changed?.Invoke(); });
        var saved = ReadToken();
        AccountName = saved?.Account;
        Profile = saved is null ? null : SteamProfiles.Cached(paths);
        if (saved is not null && SteamProfiles.IsStale(Profile)) _ = RefreshProfileAsync(saved.Token);
    }

    private async Task RefreshProfileAsync(string token)
    {
        if (_http is null || SteamProfiles.SteamIdOf(token) is not { } id) return;
        if (await SteamProfiles.FetchAsync(_paths, _http, id) is { } profile && ReadToken() is not null)
        {
            Profile = profile;
            Changed?.Invoke();
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        await _connecting.WaitAsync(ct);
        try
        {
            if (_client.IsConnected) return;
            if (_pump is null)
            {
                _running = true;
                _pump = new Thread(() => { while (_running) _manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(250)); })
                    { IsBackground = true, Name = "Steam callbacks" };
                _pump.Start();
            }
            _connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _client.Connect();
            await _connected.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
        finally
        {
            _connecting.Release();
        }
    }

    private async Task LogOnAsync(string account, string refreshToken, CancellationToken ct)
    {
        await ConnectAsync(ct);
        _loggedOn = new TaskCompletionSource<EResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _user.LogOn(new SteamUser.LogOnDetails { Username = account, AccessToken = refreshToken, ShouldRememberPassword = true });
        var result = await _loggedOn.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        if (result != EResult.OK) throw new InvalidOperationException($"Steam refused the sign-in ({result}).");
        AccountName = account;
        SignedIn = true;
        Changed?.Invoke();
    }

    public async Task<bool> ResumeAsync(CancellationToken ct = default)
    {
        if (SignedIn && _client.IsConnected) return true;
        if (ReadToken() is not { } saved) return false;
        try
        {
            await LogOnAsync(saved.Account, saved.Token, ct);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public async Task SignInAsync(string username, string password, IAuthenticator guard, CancellationToken ct = default)
    {
        await ConnectAsync(ct);
        var session = await _client.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
        {
            Username = username, Password = password, IsPersistentSession = true, DeviceFriendlyName = "MultiVRC", Authenticator = guard,
        });
        var result = await session.PollingWaitForResultAsync(ct);
        await FinishAsync(result, ct);
    }

    public async Task SignInWithQrAsync(Action<string> showUrl, CancellationToken ct = default)
    {
        await ConnectAsync(ct);
        var session = await _client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails
        {
            IsPersistentSession = true, DeviceFriendlyName = "MultiVRC",
        });
        showUrl(session.ChallengeURL);
        session.ChallengeURLChanged = () => showUrl(session.ChallengeURL);
        var result = await session.PollingWaitForResultAsync(ct);
        await FinishAsync(result, ct);
    }

    private async Task FinishAsync(AuthPollResult result, CancellationToken ct)
    {
        await LogOnAsync(result.AccountName, result.RefreshToken, ct);
        SaveToken(result.AccountName, result.RefreshToken);
        Profile = null;
        SteamProfiles.Forget(_paths);
        Changed?.Invoke();
        _ = RefreshProfileAsync(result.RefreshToken);
    }

    public void SignOut()
    {
        if (SignedIn) _user.LogOff();
        SignedIn = false;
        AccountName = null;
        Profile = null;
        SteamProfiles.Forget(_paths);
        if (File.Exists(TokenFile)) File.Delete(TokenFile);
        Changed?.Invoke();
    }

    private sealed record SavedLogin(string Account, string Token);

    private void SaveToken(string account, string token)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new SavedLogin(account, token)));
        Directory.CreateDirectory(_paths.Root);
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(TokenFile, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
            return;
        }
        // No DPAPI here: the file is readable by this user only, like ~/.ssh keys.
        using var file = new FileStream(TokenFile, new FileStreamOptions
        {
            Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
        File.SetUnixFileMode(TokenFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        file.Write(bytes);
    }

    private SavedLogin? ReadToken()
    {
        try
        {
            if (!File.Exists(TokenFile)) return null;
            var bytes = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(File.ReadAllBytes(TokenFile), null, DataProtectionScope.CurrentUser)
                : File.ReadAllBytes(TokenFile);
            return JsonSerializer.Deserialize<SavedLogin>(bytes);
        }
        catch (Exception e) when (e is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    public async Task<Build> DownloadAsync(DepotRequest request, Library library, IProgress<DownloadProgress> progress, CancellationToken ct = default)
    {
        if (!await ResumeAsync(ct)) throw new InvalidOperationException("Sign in to Steam first.");
        if (library.FindBuildByManifest(request.ManifestId) is { } existing) return existing;
        var manifestId = ulong.Parse(request.ManifestId);

        progress.Report(new DownloadProgress(0, "Asking Steam for the build…"));
        var key = await _apps.GetDepotDecryptionKey(DepotId, AppId);
        if (key.Result != EResult.OK)
            throw new InvalidOperationException($"Steam would not give this account VRChat's files ({key.Result}). Add VRChat to the account's library first.");
        var code = await _content.GetManifestRequestCode(DepotId, AppId, manifestId, request.Branch, null);
        var servers = (await _content.GetServersForSteamPipe(null, null))
            .Where(s => s.Type is "SteamCache" or "CDN").OrderBy(s => s.WeightedLoad).Take(6).ToList();
        if (servers.Count == 0) throw new InvalidOperationException("Steam offered no download servers.");
        using var cdn = new SteamKit2.CDN.Client(_client);

        var manifest = await WithServerAsync(servers, server => cdn.DownloadManifestAsync(DepotId, manifestId, code, server, key.DepotKey, null, null), ct);
        var build = new Build
        {
            Id = JsonFiles.NewId(), Name = request.Name, Version = request.Version, ManifestId = request.ManifestId,
            Branch = request.Branch, SteamBuildId = request.SteamBuildId, Source = "steam", AddedAt = DateTimeOffset.UtcNow,
        };
        var target = library.Paths.BuildFiles(build.Id);
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;

        var previous = library.Builds().Where(b => b.Source == "steam" && File.Exists(HashFile(library, b.Id)))
            .OrderByDescending(b => b.Branch == request.Branch).ThenByDescending(b => b.AddedAt).FirstOrDefault();
        var previousHashes = previous is null ? [] : JsonFiles.Read<Dictionary<string, string>>(HashFile(library, previous.Id)) ?? [];

        var files = manifest.Files ?? [];
        var hashes = new Dictionary<string, string>();
        var toDownload = new List<DepotManifest.FileData>();
        long reused = 0;
        try
        {
            foreach (var file in files)
            {
                var relative = file.FileName.Replace('\\', Path.DirectorySeparatorChar);
                var path = Path.GetFullPath(Path.Combine(target, relative));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                if (file.Flags.HasFlag(EDepotFileFlag.Directory)) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var hash = Convert.ToHexString(file.FileHash);
                hashes[relative] = hash;
                var old = previous is null ? null : Path.Combine(library.Paths.BuildFiles(previous.Id), relative);
                if (old is not null && previousHashes.GetValueOrDefault(relative) == hash && File.Exists(old))
                {
                    FileLinks.LinkFile(old, path);
                    reused += (long)file.TotalSize;
                }
                else
                {
                    toDownload.Add(file);
                }
            }

            var total = toDownload.Sum(f => (long)f.TotalSize);
            long done = 0;
            var lastReport = DateTime.MinValue;
            void Report()
            {
                if (DateTime.UtcNow - lastReport < TimeSpan.FromMilliseconds(200)) return;
                lastReport = DateTime.UtcNow;
                var percent = total == 0 ? 100 : Interlocked.Read(ref done) * 100.0 / total;
                progress.Report(new DownloadProgress(percent,
                    $"{Size(Interlocked.Read(ref done))} of {Size(total)}{(reused > 0 ? $" · {Size(reused)} reused from {previous!.Name}" : "")}"));
            }

            await System.Threading.Tasks.Parallel.ForEachAsync(toDownload, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (file, token) =>
            {
                var path = Path.Combine(target, file.FileName.Replace('\\', Path.DirectorySeparatorChar));
                using var handle = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.Asynchronous, (long)file.TotalSize);
                await System.Threading.Tasks.Parallel.ForEachAsync(file.Chunks, new ParallelOptions { MaxDegreeOfParallelism = Parallel / 4 + 1, CancellationToken = token }, async (chunk, chunkToken) =>
                {
                    var buffer = new byte[chunk.UncompressedLength];
                    var written = await WithServerAsync(servers, server => cdn.DownloadDepotChunkAsync(DepotId, chunk, server, buffer, key.DepotKey, null, null), chunkToken);
                    await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, written), (long)chunk.Offset, chunkToken);
                    Interlocked.Add(ref done, written);
                    Report();
                });
            });
        }
        catch
        {
            FileLinks.DeleteTree(library.Paths.BuildDir(build.Id));
            throw;
        }

        JsonFiles.Write(HashFile(library, build.Id), hashes);
        build.SizeBytes = files.Sum(f => (long)f.TotalSize);
        library.SaveBuild(build);
        progress.Report(new DownloadProgress(100, reused > 0 ? $"Done · {Size(reused)} reused" : "Done"));
        return build;
    }

    private static string HashFile(Library library, string buildId) => Path.Combine(library.Paths.BuildDir(buildId), "files.json");

    private static async Task<T> WithServerAsync<T>(IReadOnlyList<Server> servers, Func<Server, Task<T>> request, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return await request(servers[attempt % servers.Count]); }
            catch (Exception e) when (e is HttpRequestException or SteamKitWebRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = e;
                await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), ct);
            }
        }
        throw new InvalidOperationException($"Steam's servers kept failing: {last?.Message}", last);
    }

    private static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB" : $"{bytes / (double)(1L << 20):0} MB";

    public void Dispose()
    {
        _running = false;
        if (_client.IsConnected) _client.Disconnect();
    }
}
