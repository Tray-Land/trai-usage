using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraiUsage.Services;
using TraiUsage.Usage;
using Windows.ApplicationModel;

namespace TraiUsage.Views;

/// <summary>
/// Settings, swapped into the flyout in place of <see cref="FlyoutPage"/>. There are only a
/// couple, so they don't earn a window of their own.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly Action _goBack;
    private bool _loading = true;

    public SettingsPage(Action goBack)
    {
        _goBack = goBack;
        InitializeComponent();
        VersionText.Text = $"{App.DisplayName} {GetVersion()}";
        SignInPathText.Text = ClaudeCredentials.DefaultPath();
        IReadOnlyList<string> chosen = SettingsService.TrayBars;
        foreach (CheckBox box in BarChecks())
        {
            box.IsChecked = chosen.Contains((string)box.Tag);
        }

        _loading = false;
    }

    private CheckBox[] BarChecks() =>
        [ClaudeSessionCheck, ClaudeWeeklyCheck, CopilotPremiumCheck, CopilotChatCheck, CopilotCompletionsCheck];

    private void BarCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SettingsService.TrayBars = [.. BarChecks().Where(b => b.IsChecked == true).Select(b => (string)b.Tag)];
        App.Current.RefreshTrayIcon();
    }

    /// <summary>Re-reads state that may have changed elsewhere (Windows Settings, Claude Code).</summary>
    public void OnShown()
    {
        _ = LoadStartupStateAsync();
        _ = LoadSignInAsync();
        BackButton.Focus(FocusState.Programmatic);
    }

    private static string GetVersion()
    {
        try
        {
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "(unpackaged)";
        }
    }

    private async Task LoadSignInAsync()
    {
        bool copilot = await CopilotUsageService.IsSignedInAsync();
        CopilotCard.Visibility = copilot ? Visibility.Visible : Visibility.Collapsed;
        CopilotSignInText.Text = "Signed in through Copilot CLI or your editor.";

        string text;
        bool claude = true;
        try
        {
            ClaudeCredentials? credentials = await ClaudeUsageService.ReadSignInAsync();
            claude = credentials is not null;
            text = credentials switch
            {
                null => "Claude Code isn't signed in to a Claude account.",
                { } signIn when signIn.IsExpired(DateTimeOffset.UtcNow) =>"Signed in through Claude Code. The sign-in has expired; use Claude Code once to renew it.",
                { PlanName: { } plan } => $"Signed in through Claude Code, {plan} plan.",
                _ => "Signed in through Claude Code.",
            };
        }
        catch (Exception)
        {
            text = "Claude Code's sign-in can't be read right now.";
        }

        SignInText.Text = text;
        ClaudeBars.Visibility = claude ? Visibility.Visible : Visibility.Collapsed;
        CopilotBars.Visibility = copilot ? Visibility.Visible : Visibility.Collapsed;

        // Hide the Claude card when only Copilot is set up; with neither, keep it as the place to start.
        ClaudeCard.Visibility = claude || !copilot ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadStartupStateAsync()
    {
        ShowStartupState(await StartupService.GetStateAsync());
    }

    private void ShowStartupState(StartupTaskState? state)
    {
        _loading = true;
        StartupToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

        // The user (Task Manager, Settings > Apps > Startup) or policy has the final say; the app
        // can't override it, so say where to change it instead of offering a dead toggle.
        StartupToggle.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupDescription.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Settings > Apps > Startup. Turn it on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Managed by your organization.",
            null => "Only available when the app is installed.",
            _ => "Keep the tray icon ready after you sign in.",
        };
        _loading = false;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ShowStartupState(await StartupService.SetEnabledAsync(StartupToggle.IsOn));
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _goBack();
}
