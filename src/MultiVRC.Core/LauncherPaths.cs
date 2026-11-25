using System.Runtime.InteropServices;

namespace MultiVRC.Core;

public sealed class LauncherPaths(string root)
{
    public static LauncherPaths Default => new(Environment.GetEnvironmentVariable("MULTIVRC_DATA_DIR") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiVRC"));

    public string Root { get; } = root;
    public string Builds => Path.Combine(Root, "builds");
    public string Instances => Path.Combine(Root, "instances");
    public string Templates => Path.Combine(Root, "templates");
    public string Tools => Path.Combine(Root, "tools");
    public string SharedCache => Path.Combine(Root, "shared-cache");

    public string SharedCacheFor(Instance instance) => Path.Combine(SharedCache,
        instance.Kind == InstanceKind.Official ? "vrchat" : JsonFiles.SafeName(instance.TemplateId ?? instance.Id));
    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string SwapFile => Path.Combine(Root, "swap.json");

    public string BuildDir(string buildId) => Path.Combine(Builds, buildId);
    public string BuildFiles(string buildId) => Path.Combine(Builds, buildId, "files");
    public string InstanceDir(string instanceId) => Path.Combine(Instances, instanceId);
    public string InstanceGame(string instanceId) => Path.Combine(Instances, instanceId, "game");

    public string InstanceData(string instanceId) => Path.Combine(Instances, instanceId, "data");

    public void EnsureCreated()
    {
        foreach (var dir in new[] { Root, Builds, Instances, Templates, Tools, SharedCache }) Directory.CreateDirectory(dir);
    }

    public static string LocalLow
    {
        get
        {
            var folder = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
            if (SHGetKnownFolderPath(folder, 0, IntPtr.Zero, out var path) == 0)
            {
                try { return Marshal.PtrToStringUni(path)!; }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
        }
    }

    public static string GameDataFolder => Path.Combine(LocalLow, "VRChat", "VRChat");

    public const string GameRegistryKey = @"Software\VRChat\VRChat";

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
