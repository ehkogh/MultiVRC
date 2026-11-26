using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MultiVRC.Core;

/// <summary>What a Windows session swapped in, saved as it goes so a crash can be undone.</summary>
public sealed class SwapState
{
    public required string InstanceId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public bool FolderMoved { get; set; }
    public bool JunctionMade { get; set; }
    public bool RegistryMoved { get; set; }
    public bool RegistryLoaded { get; set; }
}

/// <summary>Keeps each instance's VRChat data and settings apart while it runs.</summary>
public interface IIsolation
{
    SwapState? Current { get; }

    void Apply(Instance instance);

    void Restore();

    /// <summary>Puts things back after a session the launcher didn't see end; the instance's id, or null.</summary>
    string? Recover(Func<bool> gameRunning);

    void ForgetInstance(string instanceId);
}

/// <summary>
/// Windows: VRChat keeps its data in LocalLow and its settings in HKCU, shared by every copy of the game, so a session swaps
/// the instance's in (a junction and a registry copy) and puts the player's own back when it ends.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Isolation(LauncherPaths paths, string? dataFolder = null, string? gameKey = null, string? launcherKey = null) : IIsolation
{
    private const string OriginalSuffix = ".multivrc-original";
    public const string DefaultLauncherKey = @"Software\MultiVRC";
    private readonly string _launcherKey = launcherKey ?? DefaultLauncherKey;
    private readonly string _gameKey = gameKey ?? LauncherPaths.GameRegistryKey;
    private string OriginalRegistry => $@"{_launcherKey}\Original";
    private string InstanceRegistry(string instanceId) => $@"{_launcherKey}\Instances\{instanceId}";

    public SwapState? Current => JsonFiles.Read<SwapState>(paths.SwapFile);

    private string DataFolder { get; } = dataFolder ?? LauncherPaths.GameDataFolder;
    private string DataBackup => DataFolder + OriginalSuffix;

    public void Apply(Instance instance)
    {
        if (Current is { } existing)
            throw new InvalidOperationException($"Instance {existing.InstanceId} is still swapped in; finish or recover it first.");

        var state = new SwapState { InstanceId = instance.Id, StartedAt = DateTimeOffset.UtcNow };
        Save(state);
        try
        {
            var data = paths.InstanceData(instance.Id);
            Directory.CreateDirectory(data);
            DataFolders.PrepareCache(paths, instance, data);

            if (FileLinks.IsJunction(DataFolder)) FileLinks.RemoveJunction(DataFolder);
            if (Directory.Exists(DataFolder))
            {
                if (Directory.Exists(DataBackup))
                    throw new InvalidOperationException($"{DataBackup} already exists; recover it before launching.");
                Directory.Move(DataFolder, DataBackup);
                state.FolderMoved = true;
                Save(state);
            }
            FileLinks.CreateJunction(DataFolder, data);
            state.JunctionMade = true;
            Save(state);

            using var current = Registry.CurrentUser.OpenSubKey(_gameKey);
            if (current is not null)
            {
                Registry.CurrentUser.DeleteSubKeyTree(OriginalRegistry, throwOnMissingSubKey: false);
                using (var backup = Registry.CurrentUser.CreateSubKey(OriginalRegistry)) CopyKey(current, backup);
                state.RegistryMoved = true;
                Save(state);
                Registry.CurrentUser.DeleteSubKeyTree(_gameKey, throwOnMissingSubKey: false);
            }
            using (var saved = Registry.CurrentUser.OpenSubKey(InstanceRegistry(instance.Id)))
            {
                if (saved is not null)
                {
                    using var target = Registry.CurrentUser.CreateSubKey(_gameKey);
                    CopyKey(saved, target);
                }
            }
            state.RegistryLoaded = true;
            Save(state);
        }
        catch
        {
            Restore();
            throw;
        }
    }

    public void Restore()
    {
        if (Current is not { } state) return;

        if (state.RegistryLoaded)
        {
            Registry.CurrentUser.DeleteSubKeyTree(InstanceRegistry(state.InstanceId), throwOnMissingSubKey: false);
            using var session = Registry.CurrentUser.OpenSubKey(_gameKey);
            if (session is not null)
            {
                using var saved = Registry.CurrentUser.CreateSubKey(InstanceRegistry(state.InstanceId));
                CopyKey(session, saved);
            }
            Registry.CurrentUser.DeleteSubKeyTree(_gameKey, throwOnMissingSubKey: false);
            state.RegistryLoaded = false;
            Save(state);
        }
        if (state.RegistryMoved)
        {
            using (var backup = Registry.CurrentUser.OpenSubKey(OriginalRegistry))
            {
                if (backup is not null)
                {
                    using var target = Registry.CurrentUser.CreateSubKey(_gameKey);
                    CopyKey(backup, target);
                }
            }
            Registry.CurrentUser.DeleteSubKeyTree(OriginalRegistry, throwOnMissingSubKey: false);
            state.RegistryMoved = false;
            Save(state);
        }

        if (state.JunctionMade || FileLinks.IsJunction(DataFolder))
        {
            FileLinks.RemoveJunction(DataFolder);
            state.JunctionMade = false;
            Save(state);
        }
        if (state.FolderMoved && Directory.Exists(DataBackup) && !Directory.Exists(DataFolder))
        {
            Directory.Move(DataBackup, DataFolder);
            state.FolderMoved = false;
        }
        File.Delete(paths.SwapFile);
    }

    public string? Recover(Func<bool> gameRunning)
    {
        if (Current is not { } state || gameRunning()) return null;
        Restore();
        return state.InstanceId;
    }

    public void ForgetInstance(string instanceId) =>
        Registry.CurrentUser.DeleteSubKeyTree(InstanceRegistry(instanceId), throwOnMissingSubKey: false);

    private void Save(SwapState state) => JsonFiles.Write(paths.SwapFile, state);

    internal static void CopyKey(RegistryKey from, RegistryKey to)
    {
        foreach (var name in from.GetValueNames()) CopyValue(from, to, name);
        foreach (var child in from.GetSubKeyNames())
        {
            using var source = from.OpenSubKey(child)!;
            using var target = to.CreateSubKey(child);
            CopyKey(source, target);
        }
    }

    private const int ErrorMoreData = 234;

    private static void CopyValue(RegistryKey from, RegistryKey to, string name)
    {
        byte[] data = [];
        uint type, size = 0;
        int result;
        while (true)
        {
            result = RegQueryValueExW(from.Handle, name, IntPtr.Zero, out type, data.Length == 0 ? null : data, ref size);
            if (result == 0 && (data.Length > 0 || size == 0)) break;
            if (result != 0 && result != ErrorMoreData) throw new System.ComponentModel.Win32Exception(result, $"Could not read registry value {name}");
            data = new byte[size];
        }
        result = RegSetValueExW(to.Handle, name, 0, type, data, size);
        if (result != 0) throw new System.ComponentModel.Win32Exception(result, $"Could not write registry value {name}");
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegQueryValueExW(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, string? name, IntPtr reserved,
        out uint type, byte[]? data, ref uint size);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegSetValueExW(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, string? name, int reserved,
        uint type, byte[] data, uint size);
}
