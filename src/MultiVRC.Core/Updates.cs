namespace MultiVRC.Core;

public enum UpdateKind
{
    SteamBuild,

    Template,
}

public sealed record PendingUpdate(
    string InstanceId, UpdateKind Kind, string Summary,
    string? ManifestId, string? Branch, long? SteamBuildId, string? TemplateVersion);

public sealed class Updates(Library library, IBranchSource branches)
{
    public List<string> Problems { get; } = [];

    public DateTimeOffset? LastChecked { get; private set; }

    public async Task<List<PendingUpdate>> CheckAsync(CancellationToken ct = default)
    {
        Problems.Clear();
        foreach (var template in library.Templates().Where(t => t.SourceUrl is not null && !t.Bundled))
        {
            try { await library.InstallTemplateAsync(template.SourceUrl!, ct); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
            {
                Problems.Add($"{template.Name}: {e.Message}");
            }
        }

        IReadOnlyList<SteamBranch> steam = [];
        var instances = library.Instances();
        if (instances.Any(i => i.TrackBranch is not null) || library.Templates().Any(t => t.Build.Branch is not null))
        {
            try { steam = await branches.GetAsync(ct); }
            catch (Exception e) when (e is InvalidOperationException or TimeoutException) { Problems.Add(e.Message); }
        }

        var pending = new List<PendingUpdate>();
        foreach (var instance in instances)
        {
            var build = library.FindBuild(instance.BuildId);
            if (instance.Kind == InstanceKind.Official)
            {
                if (instance.TrackBranch is not { } name || steam.FirstOrDefault(b => b.Name == name) is not { ManifestId: { } manifest } branch) continue;
                if (build?.ManifestId == manifest) continue;
                pending.Add(new PendingUpdate(instance.Id, UpdateKind.SteamBuild, $"New {Label(name)} build {branch.BuildId}",
                    manifest, name, branch.BuildId, null));
            }
            else if (instance.TemplateId is { } templateId && library.FindTemplate(templateId) is { } template)
            {
                (string? Manifest, string? Branch, long? Build) want = template.Build switch
                {
                    { ManifestId: { Length: > 0 } m } => (m, null, null),
                    { Branch: { Length: > 0 } b } when steam.FirstOrDefault(x => x.Name == b) is { ManifestId: { } m } x => (m, b, x.BuildId),
                    _ => (build?.ManifestId, null, null),
                };
                var (wantManifest, wantBranch, wantBuild) = want;
                var newBuild = wantManifest is not null && wantManifest != build?.ManifestId;
                var newTemplate = template.Version.Length > 0 && template.Version != instance.TemplateVersion;
                if (!newBuild && !newTemplate) continue;
                var summary = newTemplate ? $"{template.Server} update {template.Version}" : $"New build for {template.Server}";
                pending.Add(new PendingUpdate(instance.Id, UpdateKind.Template, summary,
                    newBuild ? wantManifest : null, wantBranch, wantBuild, template.Version));
            }
        }
        LastChecked = DateTimeOffset.UtcNow;
        return pending;
    }

    public static string Label(string branch) => branch == "public" ? "release" : branch;
}
