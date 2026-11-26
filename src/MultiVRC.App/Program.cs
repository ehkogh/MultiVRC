using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using MultiVRC.App.Services;

namespace MultiVRC.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var options = LaunchOptions.FromArgs(args);
        if (LauncherUpdateService.InstallOnStart(options, args)) return;

        var link = LinkInbox.FromArgs(args);
        using var single = SingleInstance.Claim(options.DataDir, link);
        if (single is null) return;
        SingleInstance.Current = single;
        var inbox = new LinkInbox();
        inbox.Add(link);
        single.Listen(text =>
        {
            WindowActions.BringToFront();
            inbox.Add(text);
        });
#if !DEBUG
        if (!options.Demo && OperatingSystem.IsWindows()) _ = Task.Run(LinkRegistration.Ensure);
#endif

        // WebView2 paints white until the page's first frame; make that the app's own background instead.
        if (Environment.GetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR") is null)
            Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "FF0B0D12");

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(inbox);
        builder.Services.AddSingleton(new HttpClient { DefaultRequestHeaders = { { "User-Agent", "MultiVRC" } } });
        builder.Services.AddSingleton<LauncherState>();
        builder.Services.AddSingleton<Toasts>();
        builder.Services.AddSingleton<Tray>();
        builder.Services.AddSingleton<BackdropPictures>();
        builder.Services.AddSingleton<Backdrop>();
        builder.Services.AddSingleton<ServerLook>();
        builder.Services.AddSingleton<UpdateService>();
        builder.Services.AddSingleton<LauncherUpdateService>();
        builder.Services.AddSingleton<InstanceActions>();
        builder.RootComponents.Add<App>("app");

        var app = builder.Build();
        app.MainWindow
            .SetTitle("MultiVRC")
            .SetChromeless(WindowActions.OwnTitleBar)
            .SetSize(1360, 800)
            .SetResizable(false)
            .Center();
        var icon = Path.Combine(AppContext.BaseDirectory, "icon", OperatingSystem.IsWindows() ? "MultiVRC.ico" : "MultiVRC-256.png");
        if (File.Exists(icon)) app.MainWindow.SetIconFile(icon);
        app.MainWindow.WindowCreated += (_, _) => WindowActions.RoundCorners();
        if (OperatingSystem.IsLinux()) _ = Task.Run(() => DesktopEntry.Ensure(options));

        var devTools = Environment.GetEnvironmentVariable("MULTIVRC_DEVTOOLS_PORT") is { Length: > 0 } port && int.TryParse(port, out _);
        if (devTools) app.MainWindow.SetBrowserControlInitParameters($"--remote-debugging-port={Environment.GetEnvironmentVariable("MULTIVRC_DEVTOOLS_PORT")}");
        app.MainWindow.SetContextMenuEnabled(devTools);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            app.MainWindow.ShowMessage("MultiVRC hit an error", e.ExceptionObject.ToString());

        WindowActions.Window = app.MainWindow;

        var state = app.Services.GetRequiredService<LauncherState>();
        var tray = app.Services.GetRequiredService<Tray>();
        tray.CheckRequested += () => _ = app.Services.GetRequiredService<UpdateService>().CheckAsync(auto: true);
        state.Changed += () => tray.SetStatus(state.Runner.Running is { } playing ? $"Playing {playing.Name}" : null);
        app.MainWindow.RegisterWindowClosingHandler((_, _) =>
        {
            // Without a tray icon there'd be no way back to a hidden window.
            if (WindowActions.Quitting || !Tray.Supported || !state.Settings.CloseToBackground) return false;
            WindowActions.Hide();
            if (!state.Settings.ToldAboutBackground)
            {
                state.Settings.ToldAboutBackground = true;
                state.SaveSettings(state.Settings);
                tray.Notify(NotifyKind.Info, "MultiVRC is still running",
                    "It keeps your instances up to date in the background. Quit it from its icon by the clock, or turn this off in Settings.");
            }
            return true;
        });

        try { app.Run(); }
        finally { tray.Dispose(); }
    }
}
