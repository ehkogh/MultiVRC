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
        if (!options.Demo) _ = Task.Run(LinkRegistration.Ensure);
#endif

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(inbox);
        builder.Services.AddSingleton(new HttpClient { DefaultRequestHeaders = { { "User-Agent", "MultiVRC" } } });
        builder.Services.AddSingleton<LauncherState>();
        builder.Services.AddSingleton<Toasts>();
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
            .SetChromeless(true)
            .SetSize(1360, 800)
            .SetResizable(false)
            .Center();
        var icon = Path.Combine(AppContext.BaseDirectory, "icon", "MultiVRC.ico");
        if (File.Exists(icon)) app.MainWindow.SetIconFile(icon);
        app.MainWindow.WindowCreated += (_, _) => WindowActions.RoundCorners();

        var devTools = Environment.GetEnvironmentVariable("MULTIVRC_DEVTOOLS_PORT") is { Length: > 0 } port && int.TryParse(port, out _);
        if (devTools) app.MainWindow.SetBrowserControlInitParameters($"--remote-debugging-port={Environment.GetEnvironmentVariable("MULTIVRC_DEVTOOLS_PORT")}");
        app.MainWindow.SetContextMenuEnabled(devTools);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            app.MainWindow.ShowMessage("MultiVRC hit an error", e.ExceptionObject.ToString());

        WindowActions.Window = app.MainWindow;

        app.Run();
    }
}
