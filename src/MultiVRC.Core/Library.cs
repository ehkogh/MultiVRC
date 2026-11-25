using System.IO.Compression;
using System.Security.Cryptography;

namespace MultiVRC.Core;

public sealed class Library(LauncherPaths paths, HttpClient http)
{
    public LauncherPaths Paths => paths;

    public List<Build> Builds() => [.. Folders(paths.Builds).Select(d => JsonFiles.Read<Build>(Path.Combine(d, "build.json"))).OfType<Build>()
        .OrderByDescending(b => b.AddedAt)];

    private static IEnumerable<string> Folders(string dir) => Directory.Exists(dir) ? Directory.EnumerateDirectories(dir) : [];

    public Build? FindBuild(string id) => JsonFiles.Read<Build>(Path.Combine(paths.BuildDir(id), "build.json"));

    public Build? FindBuildByManifest(string manifestId) => Builds().FirstOrDefault(b => b.ManifestId == manifestId);

    public void SaveBuild(Build build) => JsonFiles.Write(Path.Combine(paths.BuildDir(build.Id), "build.json"), build);

    public Build ImportFolder(string folder, string name, string version, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(Path.Combine(folder, "VRChat.exe")))
            throw new InvalidOperationException($"{folder} has no VRChat.exe: pick the folder the game is in.");
        var build = new Build { Id = JsonFiles.NewId(), Name = name, Version = version, Source = "folder", AddedAt = DateTimeOffset.UtcNow };
        try
        {
            FileLinks.LinkTree(folder, paths.BuildFiles(build.Id), progress, ct);
        }
        catch
        {
            FileLinks.DeleteTree(paths.BuildDir(build.Id));
            throw;
        }
        build.SizeBytes = FileLinks.SizeOf(paths.BuildFiles(build.Id));
        SaveBuild(build);
        return build;
    }

    public void DeleteBuild(string id)
    {
        if (Instances().FirstOrDefault(i => i.BuildId == id) is { } user)
            throw new InvalidOperationException($"{user.Name} uses this build; delete or move that instance first.");
        FileLinks.DeleteTree(paths.BuildDir(id));
    }

    public List<Instance> Instances() => [.. Folders(paths.Instances).Select(d => JsonFiles.Read<Instance>(Path.Combine(d, "instance.json"))).OfType<Instance>()
        .OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAt)];

    public void Reorder(IReadOnlyList<string> instanceIds)
    {
        foreach (var instance in Instances())
        {
            var place = instanceIds.ToList().IndexOf(instance.Id);
            var order = place < 0 ? int.MaxValue / 2 : place;
            if (instance.SortOrder == order) continue;
            instance.SortOrder = order;
            SaveInstance(instance);
        }
    }

    public Instance? FindInstance(string id) => JsonFiles.Read<Instance>(Path.Combine(paths.InstanceDir(id), "instance.json"));

    public void SaveInstance(Instance instance) => JsonFiles.Write(Path.Combine(paths.InstanceDir(instance.Id), "instance.json"), instance);

    public async Task<Instance> CreateInstanceAsync(string name, string buildId, Template? template,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (FindBuild(buildId) is not { } build) throw new InvalidOperationException("That build is not in the library.");
        var instance = new Instance
        {
            Id = JsonFiles.NewId(), Name = name.Trim(), BuildId = build.Id, CreatedAt = DateTimeOffset.UtcNow,
            Kind = template?.Kind ?? InstanceKind.Official,
            TemplateId = template?.Id, Server = template?.Server is { Length: > 0 } server ? server : "VRChat",
            Executable = template?.Executable ?? "VRChat.exe", Arguments = [.. template?.Arguments ?? []],
            ShareCache = template?.ShareCache ?? (template?.Kind ?? InstanceKind.Official) == InstanceKind.Official, Accent = template?.Accent ?? "#3fb68b",
            TemplateVersion = template?.Version,
            SortOrder = Instances().Select(i => i.SortOrder).DefaultIfEmpty(-1).Max() + 1,
            TrackBranch = (template?.Kind ?? InstanceKind.Official) == InstanceKind.Official && build.Source == "steam" ? build.Branch ?? "public" : null,
        };
        var game = paths.InstanceGame(instance.Id);
        try
        {
            progress?.Report($"Linking {build.Name}…");
            FileLinks.LinkTree(paths.BuildFiles(build.Id), game, ct: ct);
            Directory.CreateDirectory(paths.InstanceData(instance.Id));
            if (template is not null) await ApplyTemplateAsync(instance, template, progress, ct);
            SaveInstance(instance);
            return instance;
        }
        catch
        {
            FileLinks.DeleteTree(paths.InstanceDir(instance.Id));
            throw;
        }
    }

    public async Task ApplyTemplateAsync(Instance instance, Template template, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var touchesFiles = template.Downloads.Count > 0 || template.Files.Count > 0 || template.Copies.Count > 0;
        if (instance.Kind == InstanceKind.Official && touchesFiles)
            throw new InvalidOperationException("An official instance's files are never changed (Easy Anti-Cheat would flag it). Make the template modded.");

        var game = paths.InstanceGame(instance.Id);
        foreach (var download in template.Downloads)
        {
            progress?.Report($"Downloading {Path.GetFileName(new Uri(download.Url).LocalPath)}…");
            var bytes = await http.GetByteArrayAsync(download.Url, ct);
            if (download.Sha256 is { Length: > 0 } expected && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{download.Url} is not the file the template expects (SHA-256 mismatch). Not installed.");
            if (download.Extract)
            {
                using var zip = new ZipArchive(new MemoryStream(bytes));
                var root = InsideGame(game, download.Path);
                foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
                {
                    var target = InsideGame(root, entry.FullName);
                    using var source = entry.Open();
                    using var buffer = new MemoryStream();
                    await source.CopyToAsync(buffer, ct);
                    FileLinks.WriteOwnCopy(target, buffer.ToArray());
                }
            }
            else
            {
                FileLinks.WriteOwnCopy(InsideGame(game, download.Path), bytes);
            }
        }
        foreach (var file in template.Files)
        {
            progress?.Report($"Writing {file.Path}…");
            FileLinks.WriteOwnCopy(InsideGame(game, file.Path), file.Content);
        }
        foreach (var copy in template.Copies)
        {
            var from = InsideGame(game, copy.From);
            var to = InsideGame(game, copy.To);
            if (copy.Junction) { FileLinks.RemoveJunction(to); FileLinks.CreateJunction(to, from); }
            else FileLinks.CopyOwn(from, to);
        }
    }

    public async Task SwitchBuildAsync(Instance instance, string buildId, Template? template, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (FindBuild(buildId) is not { } build) throw new InvalidOperationException("That build is not in the library.");
        var game = paths.InstanceGame(instance.Id);
        var staging = game + ".new";
        FileLinks.DeleteTree(staging);
        progress?.Report($"Linking {build.Name}…");
        FileLinks.LinkTree(paths.BuildFiles(build.Id), staging, ct: ct);
        FileLinks.DeleteTree(game);
        Directory.Move(staging, game);
        instance.BuildId = build.Id;
        if (template is not null && instance.Kind == InstanceKind.Modded)
        {
            await ApplyTemplateAsync(instance, template, progress, ct);
            instance.TemplateVersion = template.Version;
        }
        SaveInstance(instance);
    }

    public IReadOnlyList<TemplatePlayMode> PlayModesOf(Instance instance) =>
        instance.TemplateId is { } id && FindTemplate(id) is { } template ? template.PlayModes : [];

    internal static string InsideGame(string game, string relative)
    {
        var root = Path.GetFullPath(game) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(game, relative));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full + Path.DirectorySeparatorChar != root)
            throw new InvalidOperationException($"The template names a path outside the game folder: {relative}");
        return full;
    }

    public void DeleteInstance(string id, Isolation isolation)
    {
        if (isolation.Current?.InstanceId == id) throw new InvalidOperationException("That instance is running.");
        FileLinks.DeleteTree(paths.InstanceDir(id));
        isolation.ForgetInstance(id);
    }

    public Instance DuplicateInstance(string id, string name)
    {
        if (FindInstance(id) is not { } source) throw new InvalidOperationException("No such instance.");
        var copy = JsonFiles.Read<Instance>(Path.Combine(paths.InstanceDir(id), "instance.json"))!;
        copy.Id = JsonFiles.NewId();
        copy.Name = name;
        copy.CreatedAt = DateTimeOffset.UtcNow;
        copy.LastPlayedAt = null;
        copy.SortOrder = source.SortOrder;
        copy.TotalPlayed = TimeSpan.Zero;
        FileLinks.LinkTree(paths.InstanceGame(source.Id), paths.InstanceGame(copy.Id));
        Directory.CreateDirectory(paths.InstanceData(copy.Id));
        SaveInstance(copy);
        return copy;
    }

    public List<string> AddedFiles(Instance instance)
    {
        var game = paths.InstanceGame(instance.Id);
        if (!Directory.Exists(game)) return [];
        var build = paths.BuildFiles(instance.BuildId);
        return [.. Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories)
            .Where(f => !File.Exists(Path.Combine(build, Path.GetRelativePath(game, f))) || FileLinks.LinkCount(f) == 1)
            .Select(f => Path.GetRelativePath(game, f)).Order()];
    }

    public List<Template> Templates() => [.. (Directory.Exists(paths.Templates) ? Directory.EnumerateFiles(paths.Templates, "*.json") : [])
        .Select(JsonFiles.Read<Template>).OfType<Template>().OrderBy(t => t.Name)];

    public Template? FindTemplate(string id) => JsonFiles.Read<Template>(Path.Combine(paths.Templates, $"{id}.json"));

    public void SaveTemplate(Template template)
    {
        if (template.Id.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
            throw new InvalidOperationException("A template id is letters, digits, - and _ only.");
        JsonFiles.Write(Path.Combine(paths.Templates, $"{template.Id}.json"), template);
    }

    public void DeleteTemplate(string id) => File.Delete(Path.Combine(paths.Templates, $"{id}.json"));

    public async Task<Template> InstallTemplateAsync(string url, CancellationToken ct = default)
    {
        var json = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile
            ? await File.ReadAllTextAsync(uri.LocalPath, ct)
            : await http.GetStringAsync(url, ct);
        var template = System.Text.Json.JsonSerializer.Deserialize<Template>(json, JsonFiles.Options)
                       ?? throw new InvalidOperationException("That URL did not return a template.");
        template.SourceUrl = url;
        SaveTemplate(template);
        return template;
    }
}
