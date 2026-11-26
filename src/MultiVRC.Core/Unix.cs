using System.Runtime.InteropServices;

namespace MultiVRC.Core;

/// <summary>The few libc calls .NET has no API for. Linux only (glibc, x64 or arm64).</summary>
internal static class Unix
{
    [DllImport("libc", SetLastError = true)]
    public static extern int link(string existing, string link);

    [DllImport("libc", SetLastError = true)]
    public static extern int kill(int pid, int signal);

    public const int SigTerm = 15;

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int StatRaw(string path, byte[] buffer);

    public readonly record struct Stat(long st_nlink);

    public static int stat(string path, out Stat result)
    {
        result = default;
        var buffer = new byte[256];
        int code;
        try { code = StatRaw(path, buffer); }
        catch (EntryPointNotFoundException) { return StatByTool(path, out result); }
        if (code != 0) return code;
        // struct stat: x64 has st_dev, st_ino, then a 64-bit st_nlink; arm64 (generic) has st_dev, st_ino, st_mode, then a 32-bit st_nlink.
        var links = RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? BitConverter.ToInt64(buffer, 16)
            : BitConverter.ToUInt32(buffer, 20);
        result = new Stat(links);
        return 0;
    }

    // glibc before 2.33 has no stat symbol.
    private static int StatByTool(string path, out Stat result)
    {
        result = default;
        var start = new System.Diagnostics.ProcessStartInfo("stat") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("%h");
        start.ArgumentList.Add(path);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !long.TryParse(output.Trim(), out var links)) return -1;
        result = new Stat(links);
        return 0;
    }
}
