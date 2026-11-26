namespace MultiVRC.Core;

public static class DataFolders
{
    /// <summary>Points the instance's download cache at its server's shared one, or makes it its own again.</summary>
    public static void PrepareCache(LauncherPaths paths, Instance instance, string data)
    {
        var cache = Path.Combine(data, "Cache-WindowsPlayer");
        if (instance.ShareCache)
        {
            var shared = Path.GetFullPath(paths.SharedCacheFor(instance));
            if (FileLinks.IsJunction(cache))
            {
                if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(new DirectoryInfo(cache).LinkTarget!)), Path.TrimEndingDirectorySeparator(shared), StringComparison.OrdinalIgnoreCase)) return;
                FileLinks.RemoveJunction(cache);
            }
            if (Directory.Exists(cache)) FileLinks.DeleteTree(cache);
            Directory.CreateDirectory(shared);
            FileLinks.CreateJunction(cache, shared);
        }
        else if (FileLinks.IsJunction(cache))
        {
            FileLinks.RemoveJunction(cache);
        }
    }
}

/// <summary>
/// Linux: every instance runs in its own Proton prefix, so its settings (the prefix's registry) and login are its own
/// without swapping anything, and Steam's own VRChat prefix is never touched. The prefix's VRChat data folder is a link
/// to the instance's data folder, the same place it is on Windows.
/// </summary>
public sealed class PrefixIsolation(LauncherPaths paths, string? picturesFolder = null) : IIsolation
{
    public SwapState? Current => null;

    public void Apply(Instance instance)
    {
        var data = paths.InstanceData(instance.Id);
        Directory.CreateDirectory(data);
        DataFolders.PrepareCache(paths, instance, data);

        var user = Proton.PrefixUser(paths.InstanceProton(instance.Id));
        LinkFolder(Path.Combine(user, "AppData", "LocalLow", "VRChat", "VRChat"), data);

        // Photos go where the player keeps them, the same for every instance, as on Windows.
        var pictures = picturesFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (pictures.Length > 0) LinkFolder(Path.Combine(user, "Pictures", "VRChat"), Path.Combine(pictures, "VRChat"));
    }

    private static void LinkFolder(string link, string target)
    {
        Directory.CreateDirectory(target);
        if (FileLinks.IsJunction(link))
        {
            if (Same(new DirectoryInfo(link).LinkTarget!, target)) return;
            FileLinks.RemoveJunction(link);
        }
        else if (Directory.Exists(link))
        {
            // The game made the folder itself before it was linked: keep what it wrote.
            foreach (var entry in Directory.EnumerateFileSystemEntries(link))
            {
                var to = Path.Combine(target, Path.GetFileName(entry));
                if (File.Exists(to) || Directory.Exists(to)) continue;
                if (Directory.Exists(entry)) Directory.Move(entry, to);
                else File.Move(entry, to);
            }
            FileLinks.DeleteTree(link);
        }
        FileLinks.CreateJunction(link, target);
    }

    private static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.Ordinal);

    public void Restore() { }

    public string? Recover(Func<bool> gameRunning) => null;

    public void ForgetInstance(string instanceId) { }
}
