using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MultiVRC.Core;

public static class FileLinks
{
    public static int LinkTree(string source, string target, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList();
        var copied = 0;
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, files[i]);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!CreateHardLink(destination, files[i], IntPtr.Zero))
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
        if (!CreateHardLink(destination, source, IntPtr.Zero)) File.Copy(source, destination);
    }

    public static int LinkCount(string path)
    {
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
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    public static void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
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

    public static void RemoveJunction(string path)
    {
        if (IsJunction(path)) Directory.Delete(path);
    }

    public static long SizeOf(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;

    public static void DeleteTree(string folder)
    {
        if (!Directory.Exists(folder)) return;
        if (IsJunction(folder)) { Directory.Delete(folder); return; }
        foreach (var dir in Directory.EnumerateDirectories(folder)) DeleteTree(dir);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }
        Directory.Delete(folder);
    }

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
