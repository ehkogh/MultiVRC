using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using MultiVRC.Core;

namespace MultiVRC.App.Services;

/// <summary>multivrc:// links waiting for the window to handle them, from the command line or from a second start.</summary>
public sealed class LinkInbox
{
    private readonly Queue<LinkRequest> _waiting = new();

    public event Action? Arrived;

    public void Add(string? text)
    {
        if (AppLinks.Parse(text) is not { } link) return;
        lock (_waiting) _waiting.Enqueue(link);
        Arrived?.Invoke();
    }

    public bool TryTake(out LinkRequest link)
    {
        lock (_waiting) return _waiting.TryDequeue(out link!);
    }

    public static string? FromArgs(string[] args) =>
        args.FirstOrDefault(a => a.StartsWith(AppLinks.Scheme + ":", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Keeps MultiVRC to one window per data folder: a second start hands its link to the first and exits.</summary>
public sealed class SingleInstance : IDisposable
{
    public static SingleInstance? Current { get; set; }

    private readonly Mutex _mutex;
    private readonly string _pipe;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, string pipe)
    {
        _mutex = mutex;
        _pipe = pipe;
    }

    /// <summary>Null when another MultiVRC already has this data folder and took <paramref name="link"/>.</summary>
    public static SingleInstance? Claim(string dataDir, string? link)
    {
        var name = "MultiVRC-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir).ToLowerInvariant())))[..16];
        var mutex = new Mutex(true, $@"Local\{name}", out var first);
        if (first) return new SingleInstance(mutex, name);
        mutex.Dispose();
        HandOff(name, link);
        return null;
    }

    private static void HandOff(string pipe, string? link)
    {
        // This process was started by the user (or their browser), so it may pass the foreground on.
        if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(AsfwAny);
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.WriteLine(link ?? "");
        }
        catch (Exception e) when (e is TimeoutException or IOException) { }
    }

    public void Listen(Action<string> received)
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var line = await reader.ReadLineAsync(_stop.Token);
                    if (line is not null) received(line);
                }
                catch (OperationCanceledException) { return; }
                catch (IOException) { }
            }
        });
    }

    /// <summary>Lets the next MultiVRC take over, for a restart.</summary>
    public void Dispose()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        _mutex.Dispose();
    }

    private const int AsfwAny = -1;
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}

/// <summary>Windows: points multivrc:// at this copy of MultiVRC for the current user. Linux does it in <see cref="DesktopEntry"/>.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class LinkRegistration
{
    private const string Key = @"Software\Classes\" + AppLinks.Scheme;

    public static void Ensure()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
            var command = $"\"{exe}\" \"%1\"";
            using (var existing = Registry.CurrentUser.OpenSubKey(Key + @"\shell\open\command"))
                if (existing?.GetValue("") as string == command) return;
            using var root = Registry.CurrentUser.CreateSubKey(Key);
            root.SetValue("", "URL:MultiVRC");
            root.SetValue("URL Protocol", "");
            using (var icon = root.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
            using (var open = root.CreateSubKey(@"shell\open\command")) open.SetValue("", command);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }
}
