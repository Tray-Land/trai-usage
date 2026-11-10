using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        RenderProviderOrder();

        _loading = false;
    }

    private CheckBox[] BarChecks() =>
        [ClaudeSessionCheck, ClaudeWeeklyCheck, CopilotPremiumCheck, CopilotChatCheck, CopilotCompletionsCheck, CodexSessionCheck, CodexWeeklyCheck];

    private void BarCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SettingsService.TrayBars = [.. BarChecks().Where(b => b.IsChecked == true).Select(b => (string)b.Tag)];
        App.Current.RefreshTrayIcon();
    }

    private void RenderProviderOrder()
    {
        IReadOnlyList<UsageProvider> order = SettingsService.ProviderOrder;
        ProviderOrderPanel.Children.Clear();
        for (int i = 0; i < order.Count; i++)
        {
            string name = UsageFormatter.ProviderName(order[i]);
            Grid row = new() { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });

            Button up = MoveButton($"Move {name} up", "Move up", "", i, -1, enabled: i > 0);
            Grid.SetColumn(up, 1);
            row.Children.Add(up);
            Button down = MoveButton($"Move {name} down", "Move down", "", i, 1, enabled: i < order.Count - 1);
            Grid.SetColumn(down, 2);
            row.Children.Add(down);
            ProviderOrderPanel.Children.Add(row);
        }
    }

    private Button MoveButton(string automationName, string toolTip, string glyph, int index, int offset, bool enabled)
    {
        Button button = new()
        {
            Width = 32,
            Height = 28,
            Padding = new Thickness(0),
            IsEnabled = enabled,
            Content = new FontIcon { FontSize = 12, Glyph = glyph },
        };
        AutomationProperties.SetName(button, automationName);
        ToolTipService.SetToolTip(button, toolTip);
        button.Click += (_, _) => MoveProvider(index, offset);
        return button;
    }

    private void MoveProvider(int index, int offset)
    {
        SettingsService.ProviderOrder =
            ProviderOrder.Move(SettingsService.ProviderOrder, index, offset);
        RenderProviderOrder();
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

        bool codex;
        string codexText;
        try
        {
            codex = await CodexUsageService.ReadSignInAsync() is not null;
            codexText = "Signed in through Codex with ChatGPT.";
        }
        catch (IOException)
        {
            codex = true;
            codexText = "Codex's sign-in can't be read right now.";
        }

        CodexCard.Visibility = codex ? Visibility.Visible : Visibility.Collapsed;
        CodexSignInText.Text = codexText;
        CodexBars.Visibility = codex ? Visibility.Visible : Visibility.Collapsed;

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

        // Hide the Claude card when only another provider is set up; with none, keep it as the place to start.
        ClaudeCard.Visibility = claude || !(copilot || codex) ? Visibility.Visible : Visibility.Collapsed;
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
