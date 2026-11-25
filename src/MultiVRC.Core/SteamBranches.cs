using SteamKit2;

namespace MultiVRC.Core;

public sealed record SteamBranch(string Name, string Description, long BuildId, string? ManifestId, DateTimeOffset Updated, bool PasswordRequired);

public interface IBranchSource
{
    Task<IReadOnlyList<SteamBranch>> GetAsync(CancellationToken ct = default);
}

public sealed class SteamBranches : IBranchSource
{
    public const string AppId = "438100", WindowsDepot = "438101";

    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, IReadOnlyList<SteamBranch> Branches)? _cached;

    public async Task<IReadOnlyList<SteamBranch>> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is { } cached && DateTimeOffset.UtcNow - cached.At < Fresh) return cached.Branches;
            var branches = await Task.Run(() => Fetch(ct), ct);
            _cached = (DateTimeOffset.UtcNow, branches);
            return branches;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IReadOnlyList<SteamBranch> Fetch(CancellationToken ct)
    {
        var client = new SteamClient();
        var manager = new CallbackManager(client);
        var user = client.GetHandler<SteamUser>()!;
        var apps = client.GetHandler<SteamApps>()!;
        IReadOnlyList<SteamBranch>? result = null;
        Exception? failure = null;
        var done = false;
        var appId = uint.Parse(AppId);

        manager.Subscribe<SteamClient.ConnectedCallback>(_ => user.LogOnAnonymous());
        manager.Subscribe<SteamClient.DisconnectedCallback>(_ => done = true);
        manager.Subscribe<SteamUser.LoggedOnCallback>(async logon =>
        {
            try
            {
                if (logon.Result != EResult.OK) throw new InvalidOperationException($"Steam refused an anonymous login ({logon.Result}).");
                var tokens = await apps.PICSGetAccessTokens(appId, null);
                tokens.AppTokens.TryGetValue(appId, out var token);
                var info = await apps.PICSGetProductInfo(new SteamApps.PICSRequest(appId, token), null);
                var app = info.Results!.SelectMany(r => r.Apps.Values).First();
                result = Read(app.KeyValues);
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                user.LogOff();
                done = true;
            }
        });

        client.Connect();
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!done && DateTime.UtcNow < until && !ct.IsCancellationRequested) manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(500));
        client.Disconnect();
        ct.ThrowIfCancellationRequested();
        if (failure is not null) throw new InvalidOperationException($"Could not read VRChat's branches from Steam: {failure.Message}", failure);
        return result ?? throw new TimeoutException("Steam did not answer in time.");
    }

    public static IReadOnlyList<SteamBranch> Read(KeyValue app)
    {
        var depots = app["depots"];
        var manifests = depots[WindowsDepot]["manifests"];
        return [.. depots["branches"].Children.Select(branch =>
        {
            var manifest = manifests[branch.Name!];
            var gid = manifest["gid"].Value ?? manifest.Value;
            long.TryParse(branch["buildid"].Value, out var build);
            long.TryParse(branch["timeupdated"].Value, out var updated);
            return new SteamBranch(branch.Name!, branch["description"].Value ?? "", build, gid is { Length: > 0 } ? gid : null,
                DateTimeOffset.FromUnixTimeSeconds(updated), branch["pwdrequired"].Value is "1" or "true");
        }).OrderBy(b => b.Name == "public" ? 0 : 1).ThenByDescending(b => b.Updated)];
    }
}
