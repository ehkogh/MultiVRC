using System.Diagnostics;
using System.Runtime.InteropServices;
using Photino.NET;

namespace MultiVRC.App.Services;

public static class WindowActions
{
    public static PhotinoWindow? Window { get; set; }

    /// <summary>Windows: a chromeless window with MultiVRC's own buttons. Elsewhere the desktop's title bar is used.</summary>
    public static bool OwnTitleBar => OperatingSystem.IsWindows();

    public static async Task<string?> PickFolderAsync(string title)
    {
        if (Window is null) return null;
        var picked = await Window.ShowOpenFolderAsync(title);
        return picked is { Length: > 0 } ? picked[0] : null;
    }

    public static async Task<string?> PickImageAsync(string title)
    {
        if (Window is null) return null;
        var picked = await Window.ShowOpenFileAsync(title, null, false, [("Pictures", ["png", "jpg", "jpeg", "webp", "gif"])]);
        return picked is { Length: > 0 } ? picked[0] : null;
    }

    public static async Task<string[]> PickImagesAsync(string title)
    {
        if (Window is null) return [];
        return await Window.ShowOpenFileAsync(title, null, true, [("Pictures", ["png", "jpg", "jpeg", "webp", "gif", "bmp"])]) ?? [];
    }

    public static void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        else
        {
            var start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(path);
            try { Process.Start(start); }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    public static void OpenUrl(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public static void Minimize() => Window?.SetMinimized(true);

    public static void BringToFront()
    {
        if (Window is not { } window) return;
        if (!OperatingSystem.IsWindows())
        {
            window.Invoke(() => window.SetMinimized(false));
            return;
        }
        window.Invoke(() =>
        {
            if (!IsWindowVisible(window.WindowHandle)) ShowWindow(window.WindowHandle, SwShow);
            if (IsIconic(window.WindowHandle)) ShowWindow(window.WindowHandle, SwRestore);
            SetForegroundWindow(window.WindowHandle);
        });
    }

    /// <summary>Closes the window; with "keep running in the background" on, that only hides it.</summary>
    public static void Close() => Window?.Close();

    /// <summary>Set once MultiVRC is really exiting, so closing the window no longer just hides it.</summary>
    public static bool Quitting { get; private set; }

    public static void Quit()
    {
        Quitting = true;
        if (Window is { } window) window.Invoke(window.Close);
    }

    public static void Hide()
    {
        if (OperatingSystem.IsWindows() && Window is { } window) ShowWindow(window.WindowHandle, SwHide);
    }

    /// <summary>The window is showing (minimised still counts), rather than closed to the background.</summary>
    public static bool IsOpen => Window is { } window && (!OperatingSystem.IsWindows() || IsWindowVisible(window.WindowHandle));

    public static void BeginMove()
    {
        if (!OperatingSystem.IsWindows() || Window is not { } window) return;
        ReleaseCapture();
        SendMessage(window.WindowHandle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    public static void RoundCorners()
    {
        if (!OperatingSystem.IsWindows() || Window is not { } window) return;
        var preference = DwmRound;
        _ = DwmSetWindowAttribute(window.WindowHandle, DwmWindowCornerPreference, ref preference, sizeof(int));
    }

    private const int WmNcLButtonDown = 0x00A1, HtCaption = 2;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmRound = 2;
    private const int SwHide = 0, SwShow = 5, SwRestore = 9;

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
