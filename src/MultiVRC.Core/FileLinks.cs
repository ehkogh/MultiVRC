using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MultiVRC.Core;

/// <summary>
/// Hard links for game files and folder links for data, on Windows (hard links, junctions) and Linux (link(2), symlinks).
/// Nothing here follows a folder link while walking a tree: a Proton prefix links its z: drive to /.
/// </summary>
public static class FileLinks
{
    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true,
    };

    public static int LinkTree(string source, string target, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var files = Directory.EnumerateFiles(source, "*", Walk).ToList();
        var copied = 0;
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, files[i]);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!HardLink(files[i], destination))
            {
                File.Copy(files[i], destination, overwrite: true);
                copied++;
            }
            if (i % 200 == 0) progress?.Report((i, files.Count));
        }
        progress?.Report((files.Count, files.Count));
        return copied;
    }

    public static void LinkFile(string source, string destination)
    {
        if (File.Exists(destination)) File.Delete(destination);
        if (!HardLink(source, destination)) File.Copy(source, destination);
    }

    public static int LinkCount(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (Unix.stat(path, out var stat) != 0) throw new IOException($"Could not read {path}.");
            return (int)stat.st_nlink;
        }
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle.DangerousGetHandle(), out var info)) throw new Win32Exception();
        return (int)info.NumberOfLinks;
    }

    public static void WriteOwnCopy(string path, ReadOnlySpan<byte> content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Unlink(path);
        File.WriteAllBytes(path, content.ToArray());
    }

    public static void WriteOwnCopy(string path, string content) => WriteOwnCopy(path, System.Text.Encoding.UTF8.GetBytes(content));

    public static void CopyOwn(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Unlink(to);
        File.Copy(from, to);
    }

    private static void Unlink(string path)
    {
        if (!File.Exists(path)) return;
        if (!IsLink(path)) File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    /// <summary>Makes <paramref name="link"/> a folder that is really <paramref name="target"/>: a junction on Windows, a symlink elsewhere.</summary>
    public static void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, Path.GetFullPath(target));
            return;
        }
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, Path.GetFullPath(target) }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !IsJunction(link)) throw new IOException($"Could not link {link} to {target}: {error.Trim()}");
    }

    public static bool IsJunction(string path) =>
        Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is not null;

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;

    public static void RemoveJunction(string path)
    {
        if (!IsJunction(path)) return;
        if (OperatingSystem.IsWindows()) Directory.Delete(path);
        else File.Delete(path);
    }

    public static long SizeOf(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", Walk).Sum(f => new FileInfo(f).Length) : 0;

    public static void DeleteTree(string folder)
    {
        if (!Directory.Exists(folder)) return;
        if (IsJunction(folder)) { RemoveJunction(folder); return; }
        foreach (var dir in Directory.EnumerateDirectories(folder)) DeleteTree(dir);
        foreach (var file in Directory.EnumerateFiles(folder)) Unlink(file);
        // Links to things that aren't there (a prefix's com1 → /dev/ttyS0, a removed drive) aren't listed as files.
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder)) File.Delete(entry);
        Directory.Delete(folder);
    }

    private static bool HardLink(string existing, string link) =>
        OperatingSystem.IsWindows() ? CreateHardLink(link, existing, IntPtr.Zero) : Unix.link(existing, link) == 0;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(IntPtr file, out ByHandleFileInformation info);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
}
