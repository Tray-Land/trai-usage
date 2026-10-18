using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraiUsage.Services;
using TraiUsage.Usage;
using TraiUsage.Views;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using WinUIEx;

namespace TraiUsage;

/// <summary>
/// Tray-only application: no window at launch, just a notification-area icon that toggles the
/// flyout. Windows exist only while they're in use; the idle app is an icon and little else.
/// </summary>
public partial class App : Application
{
    public const string DisplayName = "Trai Usage";

    private const string MutexName = "Local\\TraiUsage_SingleInstance";
    private const string ShowEventName = "Local\\TraiUsage_ShowFlyout";
    private const int MaxTooltipLength = 127; // NOTIFYICONDATA.szTip limit

    // Startup leaves JIT and first-request state behind that the idle app never touches again.
    private static readonly TimeSpan StartupTrimDelay = TimeSpan.FromSeconds(30);

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private TrayIcon? _trayIcon;
    private TrayFlyoutWindow? _flyout;
    private DispatcherQueue? _dispatcher;
    private bool _isExiting;
    private IReadOnlyList<UsageReport> _usageReports = [];
    private HICON _usageIcon;

    public App()
    {
        InitializeComponent();

        // A tray app lives on after its windows close; only the Exit menu ends it.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
    }

    public static new App Current => (App)Application.Current;

    /// <summary>Polls usage for the life of the app; null until launch completes.</summary>
    internal UsageRefresher Usage { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance owns the tray icon: ask it to open its flyout, then quit.
            try
            {
                // Let the running instance take the foreground when it shows its flyout.
                Windows.Win32.PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
                using EventWaitHandle existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch
            {
                // The other instance may be shutting down; nothing to do.
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Exit();
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => _dispatcher.TryEnqueue(ShowFlyout), null, Timeout.Infinite, executeOnlyOnce: false);

        InitializeTrayIcon();

        // The tray icon shows the numbers, so they refresh in the background, flyout open or not.
        // Detection also lets the tray menu list just the services that are set up.
        Usage = new UsageRefresher(_dispatcher);
        Usage.Start();

        // First run: show where the app lives instead of launching into silence.
        if (!SettingsService.HasLaunchedBefore)
        {
            SettingsService.HasLaunchedBefore = true;
            ShowFlyout();
        }
        else
        {
            MemoryService.ReleaseIdle(StartupTrimDelay);
        }
    }

    /// <summary>Sets the tray tooltip under the app name, e.g. "3 new" or null for just the name.</summary>
    public void SetTrayStatus(string? status)
    {
        if (_trayIcon is null)
        {
            return;
        }

        string tooltip = string.IsNullOrWhiteSpace(status) ? DisplayName : $"{DisplayName}\n{status}";
        _trayIcon.Tooltip = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
    }

    /// <summary>
    /// Draws the session and weekly bars as the tray icon from the latest fetched report.
    /// </summary>
    internal void SetTrayUsage(IReadOnlyList<UsageReport> reports)
    {
        _usageReports = reports;
        DrawUsageIcon();
    }

    /// <summary>Redraws the tray icon after a tray setting changed.</summary>
    internal void RefreshTrayIcon() => DrawUsageIcon();

    private void DrawUsageIcon()
    {
        if (_trayIcon is null || _usageReports.Count == 0)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool light = SystemThemeService.IsLight;
        TrayBar BarFor(UsageLimit? source)
        {
            if (source?.AsOf(now) is not { } limit)
            {
                return new TrayBar(0, 0);
            }

            return new TrayBar(limit.Percent / 100, SeverityColor(UsagePace.For(limit, now).Severity, light));
        }

        TrayBar[] bars = [.. TrayBarSelection.Select(_usageReports, SettingsService.TrayBars).Select(BarFor)];
        if (bars.Length == 0)
        {
            _trayIcon.SetIcon(TrayIconPath); // Nothing chosen (or nothing to show): the app glyph.
            return;
        }

        int size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());
        byte[] image = TrayBarsIcon.Create(size, bars, light ? 0x40000000u : 0x50FFFFFFu);

        HICON icon;
        nint handle;
        unsafe
        {
            fixed (byte* bits = image)
            {
                icon = PInvoke.CreateIconFromResourceEx(
                    bits, (uint)image.Length, true, 0x00030000, size, size, IMAGE_FLAGS.LR_DEFAULTCOLOR);
            }

            handle = (nint)icon.Value;
        }

        if (handle == 0)
        {
            return;
        }

        // The shell copies the image when the icon is set, so the previous handle can go afterwards.
        _trayIcon.SetIcon(Win32Interop.GetIconIdFromIcon(handle));
        ReleaseUsageIcon();
        _usageIcon = icon;
    }

    private void ReleaseUsageIcon()
    {
        if (!_usageIcon.IsNull)
        {
            PInvoke.DestroyIcon(_usageIcon);
            _usageIcon = default;
        }
    }

    // The same colors as the flyout's bars (accent, caution, critical), picked for the taskbar's theme.
    private static uint SeverityColor(UsageSeverity severity, bool light)
    {
        switch (severity)
        {
            case UsageSeverity.Caution:
                return light ? 0xFF9D5D00 : 0xFFFCE100;
            case UsageSeverity.Critical:
                return light ? 0xFFC42B1C : 0xFFFF99A4;
            default:
                Windows.UI.Color accent = new Windows.UI.ViewManagement.UISettings().GetColorValue(
                    light ? Windows.UI.ViewManagement.UIColorType.AccentDark1 : Windows.UI.ViewManagement.UIColorType.AccentLight2);
                return 0xFF000000u | ((uint)accent.R << 16) | ((uint)accent.G << 8) | accent.B;
        }
    }

    public void ShowFlyout()
    {
        TrayFlyoutWindow flyout = EnsureFlyout();
        flyout.HidePopup();
        flyout.ShowMainPage();
        flyout.ShowPopup();
    }

    /// <summary>Shows the flyout on its settings page.</summary>
    public void ShowSettings()
    {
        TrayFlyoutWindow flyout = EnsureFlyout();
        flyout.ShowSettingsPage();
        if (!flyout.IsPopupVisible)
        {
            flyout.ShowPopup();
        }
    }

    /// <summary>Opens a provider's own usage page in the browser.</summary>
    internal void OpenUsagePage(UsageProvider provider)
    {
        _flyout?.HidePopup();
        _ = Windows.System.Launcher.LaunchUriAsync(UsageProviders.UsagePageUri(provider));
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TrayIcon(1, TrayIconPath, DisplayName);
        _trayIcon.Selected += (_, _) => EnsureFlyout().Toggle();
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
        WindowPlacementService.SetTrayIcon(_trayIcon);
        SystemThemeService.Changed += (_, _) => _dispatcher?.TryEnqueue(() =>
        {
            if (_usageReports.Count == 0)
            {
                _trayIcon?.SetIcon(TrayIconPath);
            }
            else
            {
                DrawUsageIcon();
            }
        });
    }

    // A white glyph disappears on a light taskbar, which follows the Windows theme, not the app theme.
    private static string TrayIconPath => Path.Combine(
        AppContext.BaseDirectory, "Assets", SystemThemeService.IsLight ? "AppIcon-dark.ico" : "AppIcon.ico");

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        MenuFlyout menu = new();
        MenuFlyoutItem open = new() { Text = $"Open {DisplayName}", Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => ShowFlyout();
        MenuFlyoutItem settings = new() { Text = "Settings", Icon = new FontIcon { Glyph = "" } };
        settings.Click += (_, _) => ShowSettings();
        MenuFlyoutItem exit = new() { Text = "Exit", Icon = new FontIcon { Glyph = "" } };
        exit.Click += (_, _) => ExitApp();

        menu.Items.Add(open);

        // Only the services that are set up; before the first check, offer both.
        IReadOnlyList<UsageProvider> providers = UsageProviders.Active.Count > 0
            ? UsageProviders.Active
            : [UsageProvider.Claude, UsageProvider.Copilot];
        foreach (UsageProvider provider in providers)
        {
            MenuFlyoutItem usagePage = new()
            {
                Text = provider == UsageProvider.Copilot ? "Open usage on github.com" : "Open usage on claude.ai",
                Icon = new FontIcon { Glyph = "" },
            };
            usagePage.Click += (_, _) => OpenUsagePage(provider);
            menu.Items.Add(usagePage);
        }

        menu.Items.Add(settings);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);
        args.Flyout = menu;
    }

    private TrayFlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            // The flyout closes itself after staying hidden for a while; the next open builds a new one.
            _flyout = new TrayFlyoutWindow();
            _flyout.Closed += (_, _) =>
            {
                _flyout = null;
                ReleaseIdleResourcesIfNoWindows();
            };
        }

        return _flyout;
    }

    private void ReleaseIdleResourcesIfNoWindows()
    {
        if (_flyout is null && !_isExiting)
        {
            MemoryService.ReleaseIdle();
        }
    }

    private void ExitApp()
    {
        _isExiting = true;
        _flyout?.CloseWindow();

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        ReleaseUsageIcon();

        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Exit();
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "TraiUsage-crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // Never throw from the crash logger.
        }
    }
}
