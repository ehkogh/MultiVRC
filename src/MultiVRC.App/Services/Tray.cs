#if WINDOWS
using Forms = System.Windows.Forms;
#endif

namespace MultiVRC.App.Services;

public enum NotifyKind
{
    UpdateAvailable,
    UpdateStarted,
    UpdateFinished,
    UpdateFailed,
    LauncherUpdate,
    Info,
    Test,
}

#if WINDOWS
/// <summary>MultiVRC's icon by the clock: it keeps the launcher reachable while the window is closed, and shows its notifications.</summary>
public sealed class Tray : IDisposable
{
    public static bool Supported => true;

    private readonly LauncherState _state;
    private readonly ManualResetEventSlim _ready = new();
    private Forms.Control? _ui;
    private Forms.NotifyIcon? _icon;
    private string? _clickTarget;

    /// <summary>The window should come back, at this page if one is given.</summary>
    public event Action<string?>? OpenRequested;

    public event Action? CheckRequested;

    public Tray(LauncherState state)
    {
        _state = state;
        var thread = new Thread(Run) { IsBackground = true, Name = "MultiVRC tray" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        _ui = new Forms.Control();
        _ = _ui.Handle;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open MultiVRC", null, (_, _) => Open(null));
        menu.Items.Add("Check for updates", null, (_, _) => CheckRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit MultiVRC", null, (_, _) => WindowActions.Quit());
        var iconFile = Path.Combine(AppContext.BaseDirectory, "icon", "MultiVRC.ico");
        _icon = new Forms.NotifyIcon
        {
            Icon = File.Exists(iconFile) ? new System.Drawing.Icon(iconFile) : System.Drawing.SystemIcons.Application,
            Text = _state.Demo ? "MultiVRC (demo)" : "MultiVRC",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Open(null); };
        _icon.BalloonTipClicked += (_, _) => Open(_clickTarget);
        _ready.Set();
        Forms.Application.Run();
    }

    private void Open(string? target)
    {
        WindowActions.BringToFront();
        OpenRequested?.Invoke(target);
    }

    public static bool Wants(NotificationSettings settings, NotifyKind kind) => kind switch
    {
        NotifyKind.Test => true,
        _ when !settings.Enabled => false,
        NotifyKind.UpdateAvailable => settings.UpdateAvailable,
        NotifyKind.UpdateStarted => settings.UpdateStarted,
        NotifyKind.UpdateFinished => settings.UpdateFinished,
        NotifyKind.UpdateFailed => settings.UpdateFailed,
        NotifyKind.LauncherUpdate => settings.LauncherUpdate,
        _ => true,
    };

    /// <param name="target">The page to show when the notification is clicked.</param>
    public void Notify(NotifyKind kind, string title, string text, string? target = null)
    {
        var settings = _state.Settings.Notifications;
        if (!Wants(settings, kind)) return;
        // While the window is open MultiVRC is "live": its own messages are enough.
        if (kind != NotifyKind.Test && WindowActions.IsOpen) return;
        Post(() =>
        {
            _clickTarget = target;
            _icon?.ShowBalloonTip(6000, title, text, kind == NotifyKind.UpdateFailed ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.None);
        });
    }

    public void SetStatus(string? status) =>
        Post(() => { if (_icon is not null) _icon.Text = Trim(status is null ? "MultiVRC" : $"MultiVRC · {status}"); });

    private static string Trim(string text) => text.Length <= 63 ? text : text[..62] + "…";

    private void Post(Action action)
    {
        if (_ui is { IsHandleCreated: true, IsDisposed: false } ui)
        {
            try { ui.BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }
    }

    public void Dispose()
    {
        if (_ui is not { IsHandleCreated: true } ui) return;
        try
        {
            ui.Invoke(() =>
            {
                if (_icon is not null) { _icon.Visible = false; _icon.Dispose(); }
                Forms.Application.ExitThread();
            });
        }
        catch (InvalidOperationException) { }
    }
}
#else
/// <summary>
/// No tray icon off Windows yet: the window closes normally and nothing is notified. Same shape as the Windows one so
/// callers needn't care.
/// </summary>
public sealed class Tray(LauncherState state) : IDisposable
{
    public static bool Supported => false;

#pragma warning disable CS0067 // Never raised without a tray icon.
    public event Action<string?>? OpenRequested;

    public event Action? CheckRequested;
#pragma warning restore CS0067

    public LauncherState State => state;

    public void Notify(NotifyKind kind, string title, string text, string? target = null) { }

    public void SetStatus(string? status) { }

    public void Dispose() { }
}
#endif
