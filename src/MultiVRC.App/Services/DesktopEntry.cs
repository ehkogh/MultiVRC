namespace MultiVRC.App.Services;

/// <summary>Linux: puts MultiVRC in the desktop's app menu, pointing at wherever this copy lives, and makes it open multivrc:// links.</summary>
public static class DesktopEntry
{
    public static void Ensure(LaunchOptions options)
    {
#if DEBUG
        return;
#else
        if (options.Demo || Environment.ProcessPath is not { } exe) return;
        try
        {
            var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            var file = Path.Combine(data, "applications", "multivrc.desktop");
            var icon = Path.Combine(AppContext.BaseDirectory, "icon", "MultiVRC-256.png");
            var entry = $"""
                [Desktop Entry]
                Type=Application
                Name=MultiVRC
                Comment=Official VRChat, betas, old builds and private servers, side by side
                Exec="{exe}" %u
                Icon={icon}
                Terminal=false
                Categories=Game;
                StartupWMClass=MultiVRC
                MimeType=x-scheme-handler/multivrc;
                """ + "\n";
            if (File.Exists(file) && File.ReadAllText(file) == entry) return;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, entry);
            Run("xdg-mime", "default", "multivrc.desktop", "x-scheme-handler/multivrc");
            Run("update-desktop-database", Path.GetDirectoryName(file)!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
#endif
    }

    private static void Run(string tool, params string[] arguments)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start);
            process?.WaitForExit(5000);
        }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
